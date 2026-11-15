using UsbToC64.Core.Emulation;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.XInput;
using Xunit;

namespace UsbToC64.Core.Tests;

public class EngineTests
{
    private static JmProfile GenericProfile() =>
        ProfileBuilder.Build(MappingProposer.Propose(ControlClassifier.Classify(Descriptors.GenericGamepad)), 1, 2);

    [Fact]
    public void Neutral_NothingActive()
    {
        var e = new DynamicEngine(GenericProfile());
        var s = e.Process(new byte[] { 128, 128, 0x08, 0, 0 })!.Value; // hat 8 = neutral
        Assert.Equal(default, s);
    }

    [Fact]
    public void HatDiagonal_And_Fire()
    {
        var e = new DynamicEngine(GenericProfile());
        var s = e.Process(new byte[] { 128, 128, 0x03, 0x01, 0 })!.Value; // SE + button 1
        Assert.True(s.Down && s.Right && s.Fire1);
        Assert.False(s.Up || s.Left);
    }

    [Fact]
    public void Axis_UsesFirmwareThresholds()
    {
        var e = new DynamicEngine(GenericProfile());
        Assert.True(e.Process(new byte[] { 63, 128, 8, 0, 0 })!.Value.Left);
        Assert.False(e.Process(new byte[] { 64, 128, 8, 0, 0 })!.Value.Left);
        Assert.False(e.Process(new byte[] { 190, 128, 8, 0, 0 })!.Value.Right);
        Assert.True(e.Process(new byte[] { 191, 128, 8, 0, 0 })!.Value.Right);
        Assert.True(e.Process(new byte[] { 128, 0, 8, 0, 0 })!.Value.Up);
    }

    [Fact]
    public void Autofire_LatchAndHold()
    {
        var e = new DynamicEngine(GenericProfile());
        // button 7 (0x40) = AUTO ON, button 6 (0x20) = AUTO OFF, button 4 (0x08) = HOLD
        Assert.True(e.Process(new byte[] { 128, 128, 8, 0x40, 0 })!.Value.Autofire);
        Assert.True(e.Process(new byte[] { 128, 128, 8, 0x00, 0 })!.Value.Autofire);  // latched
        Assert.False(e.Process(new byte[] { 128, 128, 8, 0x20, 0 })!.Value.Autofire); // off
        Assert.True(e.Process(new byte[] { 128, 128, 8, 0x08, 0 })!.Value.Autofire);  // hold
        Assert.False(e.Process(new byte[] { 128, 128, 8, 0x00, 0 })!.Value.Autofire); // released
    }

    [Fact]
    public void ShortReport_IsIgnored()
    {
        var e = new DynamicEngine(GenericProfile());
        Assert.Null(e.Process(new byte[] { 1, 2 }));
    }

    [Fact]
    public void EqRule_FailsWithTwoButtons_BitAnyDoesNot()
    {
        // Problem P4: the web SAVE generates "==" for buttons.
        var eq = new JmProfile { Vid = 1, Pid = 2 };
        eq.Rules.Add(new JmRule(10, 1, JmOp.Eq, JmFunc.Fire1));
        var bit = new JmProfile { Vid = 1, Pid = 2 };
        bit.Rules.Add(new JmRule(10, 1, JmOp.BitAny, JmFunc.Fire1));
        var report = new byte[12]; report[10] = 0x05; // A + X pressed
        Assert.False(new DynamicEngine(eq).Process(report)!.Value.Fire1);
        Assert.True(new DynamicEngine(bit).Process(report)!.Value.Fire1);
    }

    [Fact]
    public void XInput_NativeReport_FiresA()
    {
        var p = ProfileBuilder.Build(MappingProposer.Propose(XInputLayout.Describe()), 0x045E, 0x028E);
        var report = XInputLayout.BuildNativeReport((ushort)(XInputLayout.Buttons.A | XInputLayout.Buttons.DpadLeft), 0, 0, 0, 0, 0, 0);
        var s = new DynamicEngine(p).Process(report)!.Value;
        Assert.True(s.Fire1 && s.Left);
        Assert.False(s.Up || s.Right || s.Down);
    }
}

public class SaveJmJsonTests
{
    [Fact]
    public void Serialize_MatchesFirmwareFormat()
    {
        var p = new JmProfile { Vid = 1118, Pid = 654 };
        p.Rules.Add(new JmRule(10, 1, JmOp.Eq, JmFunc.Fire1));
        Assert.Equal(
            "{\"cmd\":\"save_jm\",\"vid\":1118,\"pid\":654,\"dpad_idx\":-1,\"use_analog\":0,\"analog_x\":[],\"analog_y\":[],\"rules\":[{\"i\":10,\"v\":1,\"op\":0,\"f\":8}]}",
            SaveJmJson.Serialize(p));
    }

    [Fact]
    public void RoundTrip()
    {
        var p = new JmProfile { Vid = 0x0079, Pid = 0x0006, DpadIndex = 2 };
        p.Rules.Add(new JmRule(2, 3, JmOp.Hat, JmFunc.RightDown));
        p.Rules.Add(new JmRule(3, 0x10, JmOp.BitAny, JmFunc.AutofireHold));
        p.AnalogX.Add(0); p.AnalogY.Add(1);
        var q = SaveJmJson.Parse(SaveJmJson.Serialize(p));
        Assert.Equal((p.Vid, p.Pid, p.DpadIndex), (q.Vid, q.Pid, q.DpadIndex));
        Assert.Equal(p.Rules, q.Rules);
        Assert.Equal(p.AnalogX, q.AnalogX);
        Assert.Equal(p.AnalogY, q.AnalogY);
    }

    [Fact]
    public void ThirtyTwoRules_StayUnderBufferEstimate()
    {
        var p = new JmProfile { Vid = 1, Pid = 2 };
        for (int i = 0; i < 32; i++) p.Rules.Add(new JmRule((byte)i, 255, JmOp.BitAny, JmFunc.Fire1));
        p.AnalogX.AddRange(new byte[] { 1, 2, 3, 4 }); p.AnalogY.AddRange(new byte[] { 5, 6, 7, 8 });
        Assert.InRange(SaveJmJson.EstimateArduinoJsonBytes(p), 2500, FirmwareLimits.JsonDocumentCapacity);
    }
}
