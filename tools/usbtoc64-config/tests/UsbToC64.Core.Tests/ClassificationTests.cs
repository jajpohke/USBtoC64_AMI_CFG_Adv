using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using UsbToC64.Core.XInput;
using Xunit;

namespace UsbToC64.Core.Tests;

public class ClassificationTests
{
    [Fact]
    public void GenericGamepad_ControlsAreLocated()
    {
        var pad = ControlClassifier.Classify(Descriptors.GenericGamepad);
        Assert.False(pad.UsesReportIds);

        var x = pad.Controls.Single(c => c.Id == "axis_x");
        var y = pad.Controls.Single(c => c.Id == "axis_y");
        Assert.Equal((ControlKind.Axis, 0, true), (x.Kind, x.ByteIndex, x.Supported));
        Assert.Equal(1, y.ByteIndex);

        var hat = pad.Controls.Single(c => c.Kind == ControlKind.Hat);
        Assert.Equal((2, 0, 8, true), (hat.ByteIndex, hat.HatBase, hat.HatPositions, hat.Supported));

        var buttons = pad.Controls.Where(c => c.Kind == ControlKind.Button).ToList();
        Assert.Equal(12, buttons.Count);
        Assert.Equal((3, (byte)0x01), (buttons[0].ByteIndex, buttons[0].Mask));
        Assert.Equal((3, (byte)0x80), (buttons[7].ByteIndex, buttons[7].Mask));
        Assert.Equal((4, (byte)0x08), (buttons[11].ByteIndex, buttons[11].Mask));
    }

    [Fact]
    public void GenericGamepad_AutomaticProposal()
    {
        var pad = ControlClassifier.Classify(Descriptors.GenericGamepad);
        var m = MappingProposer.Propose(pad);
        var p = ProfileBuilder.Build(m, 0x0079, 0x0006);

        // hat -> 8 HAT rules on byte 2
        var hatRules = p.Rules.Where(r => r.Op == JmOp.Hat).ToList();
        Assert.Equal(8, hatRules.Count);
        Assert.All(hatRules, r => Assert.Equal(2, r.Index));
        Assert.Equal(new JmRule(2, 0, JmOp.Hat, JmFunc.Up), hatRules[0]);
        Assert.Equal(new JmRule(2, 7, JmOp.Hat, JmFunc.LeftUp), hatRules[7]);
        Assert.Equal(2, p.DpadIndex);

        // X/Y axes -> analog
        Assert.Equal(new byte[] { 0 }, p.AnalogX);
        Assert.Equal(new byte[] { 1 }, p.AnalogY);

        // buttons -> default sequence, BITANY
        Assert.Contains(new JmRule(3, 0x01, JmOp.BitAny, JmFunc.Fire1), p.Rules);
        Assert.Contains(new JmRule(3, 0x02, JmOp.BitAny, JmFunc.Up), p.Rules);
        Assert.Contains(new JmRule(3, 0x04, JmOp.BitAny, JmFunc.Fire2), p.Rules);
        Assert.Contains(new JmRule(3, 0x40, JmOp.BitAny, JmFunc.AutofireOn), p.Rules);
        Assert.DoesNotContain(p.Rules, r => r.Index == 3 && r.Value == 0x80); // 8th button left free
    }

    [Fact]
    public void ReportId_ShiftsIndexes_And16BitAxisUsesHighByte()
    {
        var pad = ControlClassifier.Classify(Descriptors.ReportId16Bit);
        Assert.True(pad.UsesReportIds);
        Assert.Equal(2, pad.Controls.Single(c => c.Id == "axis_x").ByteIndex);
        Assert.Equal(4, pad.Controls.Single(c => c.Id == "axis_y").ByteIndex);

        var hat = pad.Controls.Single(c => c.Kind == ControlKind.Hat);
        Assert.Equal((5, 1), (hat.ByteIndex, hat.HatBase));
        Assert.Equal(6, pad.Controls.First(c => c.Kind == ControlKind.Button).ByteIndex);

        var p = ProfileBuilder.Build(MappingProposer.Propose(pad), 1, 2);
        Assert.Contains(new JmRule(5, 1, JmOp.Hat, JmFunc.Up), p.Rules);
        Assert.Contains(new JmRule(5, 8, JmOp.Hat, JmFunc.LeftUp), p.Rules);
    }

    [Fact]
    public void SignedHidAxes_UseSignedRules_OnHighByte()
    {
        var pad = ControlClassifier.Classify(Descriptors.SignedAxes);
        Assert.All(pad.Controls.Where(c => c.Kind == ControlKind.Axis), c => Assert.True(c.Supported && c.Signed));
        var p = ProfileBuilder.Build(MappingProposer.Propose(pad), 1, 2);
        Assert.False(p.UseAnalog); // no unsigned analog path
        // X: bytes 0-1 -> high byte 1; Y: bytes 2-3 -> high byte 3 (HID: positive = down)
        Assert.Contains(new JmRule(1, unchecked((byte)-51), JmOp.SignedLess, JmFunc.Left), p.Rules);
        Assert.Contains(new JmRule(1, 51, JmOp.SignedGreater, JmFunc.Right), p.Rules);
        Assert.Contains(new JmRule(3, unchecked((byte)-51), JmOp.SignedLess, JmFunc.Up), p.Rules);
        Assert.Contains(new JmRule(3, 51, JmOp.SignedGreater, JmFunc.Down), p.Rules);
        Assert.Contains(new JmRule(4, 0x01, JmOp.BitAny, JmFunc.Fire1), p.Rules);
    }

    [Fact]
    public void AxisDpad_BecomesAnalogDirections_WithoutUserChoice()
    {
        var pad = ControlClassifier.Classify(Descriptors.AxisDpad);
        var p = ProfileBuilder.Build(MappingProposer.Propose(pad), 0x0583, 0x2060);
        Assert.Equal(new byte[] { 0 }, p.AnalogX);
        Assert.Equal(new byte[] { 1 }, p.AnalogY);
        Assert.Contains(new JmRule(2, 0x01, JmOp.BitAny, JmFunc.Fire1), p.Rules);
    }

    [Fact]
    public void DpadUsages_BecomeDirections()
    {
        var pad = ControlClassifier.Classify(Descriptors.DpadUsages);
        var p = ProfileBuilder.Build(MappingProposer.Propose(pad), 1, 2);
        Assert.Contains(new JmRule(0, 0x01, JmOp.BitAny, JmFunc.Up), p.Rules);
        Assert.Contains(new JmRule(0, 0x02, JmOp.BitAny, JmFunc.Down), p.Rules);
        Assert.Contains(new JmRule(0, 0x04, JmOp.BitAny, JmFunc.Right), p.Rules);
        Assert.Contains(new JmRule(0, 0x08, JmOp.BitAny, JmFunc.Left), p.Rules);
        Assert.Contains(new JmRule(0, 0x10, JmOp.BitAny, JmFunc.Fire1), p.Rules);
    }

    [Fact]
    public void XInput_UsesNativeLayout()
    {
        var pad = XInputLayout.Describe();
        var p = ProfileBuilder.Build(MappingProposer.Propose(pad), 0x045E, 0x028E);
        Assert.Contains(new JmRule(2, 0x01, JmOp.BitAny, JmFunc.Up), p.Rules);
        Assert.Contains(new JmRule(2, 0x08, JmOp.BitAny, JmFunc.Right), p.Rules);
        Assert.Contains(new JmRule(3, 0x10, JmOp.BitAny, JmFunc.Fire1), p.Rules); // A
        Assert.Contains(new JmRule(3, 0x20, JmOp.BitAny, JmFunc.Up), p.Rules);    // B
        Assert.False(p.UseAnalog);
        // Left stick: LX high byte 7, LY high byte 9 (XInput: Y positive = up)
        Assert.Contains(new JmRule(7, unchecked((byte)-51), JmOp.SignedLess, JmFunc.Left), p.Rules);
        Assert.Contains(new JmRule(9, 51, JmOp.SignedGreater, JmFunc.Up), p.Rules);
        Assert.All(p.Rules.Where(r => !FirmwareLimits.IsSigned(r.Op)), r => Assert.InRange(r.Index, 2, 5));
        Assert.DoesNotContain(ProfileValidator.Validate(p), i => i.Code == "XINPUT_SYNTHETIC_INDEXES");
    }

    [Fact]
    public void WindowsReport_IdByteRemovedOnlyWithoutReportIds()
    {
        byte[] win = { 0x00, 0x10, 0x20 };
        Assert.Equal(new byte[] { 0x10, 0x20 }, ReportView.FromWindows(win, usesReportIds: false));
        Assert.Equal(win, ReportView.FromWindows(win, usesReportIds: true));
    }
}

public class GipTests
{
    [Fact]
    public void Gip_LayoutAndProposal()
    {
        var pad = UsbToC64.Core.XInput.XInputLayout.Describe(UsbToC64.Core.XInput.XInputKind.Gip);
        var p = UsbToC64.Core.Mapping.ProfileBuilder.Build(UsbToC64.Core.Mapping.MappingProposer.Propose(pad), 0x045E, 0x0B12);
        Assert.Contains(new UsbToC64.Core.Model.JmRule(5, 0x01, UsbToC64.Core.Model.JmOp.BitAny, UsbToC64.Core.Model.JmFunc.Up), p.Rules);
        Assert.Contains(new UsbToC64.Core.Model.JmRule(4, 0x10, UsbToC64.Core.Model.JmOp.BitAny, UsbToC64.Core.Model.JmFunc.Fire1), p.Rules); // A
        Assert.All(p.Rules.Where(r => !UsbToC64.Core.Model.FirmwareLimits.IsSigned(r.Op)), r => Assert.InRange(r.Index, 4, 9));
        Assert.Contains(p.Rules, r => r.Index == 11 && r.Op == UsbToC64.Core.Model.JmOp.SignedLess);   // LX
        Assert.Contains(p.Rules, r => r.Index == 13 && r.Op == UsbToC64.Core.Model.JmOp.SignedGreater && r.Func == UsbToC64.Core.Model.JmFunc.Up); // LY
        Assert.False(p.UseAnalog);
        var codes = UsbToC64.Core.Validation.ProfileValidator.Validate(p).Select(i => i.Code).ToList();
        Assert.Contains("GIP_FIRMWARE", codes);
        Assert.DoesNotContain("GIP_SYNTHETIC_INDEXES", codes);
    }

    [Fact]
    public void Gip_ReportFromXInputState_DrivesTheEngine()
    {
        var X = UsbToC64.Core.XInput.XInputLayout.Buttons.A | UsbToC64.Core.XInput.XInputLayout.Buttons.DpadRight;
        var report = UsbToC64.Core.XInput.XInputLayout.BuildGipReport((ushort)X, 255, 0, 0, 0, 0, 0);
        Assert.Equal(0x20, report[0]);
        Assert.Equal(0x10, report[4]);   // A
        Assert.Equal(0x08, report[5]);   // right
        Assert.Equal(0x03, report[7]);   // LT fully pressed: 1023 -> high byte 3
        var pad = UsbToC64.Core.XInput.XInputLayout.Describe(UsbToC64.Core.XInput.XInputKind.Gip);
        var m = UsbToC64.Core.Mapping.MappingProposer.Propose(pad);
        m.Set(pad.Controls.Single(c => c.Id == "trg_lt"), UsbToC64.Core.Mapping.PadAction.Fire3);
        var s = new UsbToC64.Core.Emulation.DynamicEngine(UsbToC64.Core.Mapping.ProfileBuilder.Build(m, 0x045E, 0x0B12)).Process(report)!.Value;
        Assert.True(s.Fire1 && s.Right && s.Fire3);
        Assert.False(s.Up || s.Left || s.Down);
    }

    [Fact]
    public void Gip_SyntheticIndexes_AreReported()
    {
        var p = new UsbToC64.Core.Model.JmProfile { Vid = 0x045E, Pid = 0x02EA };
        p.Rules.Add(new UsbToC64.Core.Model.JmRule(10, 1, UsbToC64.Core.Model.JmOp.BitAny, UsbToC64.Core.Model.JmFunc.Fire1));
        Assert.Contains(UsbToC64.Core.Validation.ProfileValidator.Validate(p), i => i.Code == "GIP_SYNTHETIC_INDEXES");
    }
}
