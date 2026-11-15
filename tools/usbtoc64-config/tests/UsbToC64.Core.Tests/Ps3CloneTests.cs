using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>
/// Cross-check against the hand-written firmware profile "Sony PS3 Clone"
/// (USBtoC64/JoystickProfiles.h, VID 2064 PID 1).
/// </summary>
public class Ps3CloneTests
{
    private static PadDescription Pad() => ControlClassifier.Classify(Descriptors.Ps3Clone0810);

    private static Control Btn(int n) => Pad().Controls.Single(c => c.Id == $"btn{n}");

    [Fact]
    public void Axes_MatchFirmwareProfile()
    {
        var pad = Pad();
        Assert.True(pad.UsesReportIds);
        Assert.Equal(3, pad.Controls.Single(c => c.Id == "axis_x").ByteIndex); // byte_x = 3
        Assert.Equal(4, pad.Controls.Single(c => c.Id == "axis_y").ByteIndex); // byte_y = 4
        Assert.Equal(1, pad.Controls.Single(c => c.Id == "axis_rz").ByteIndex);
        Assert.Equal(2, pad.Controls.Single(c => c.Id == "axis_z").ByteIndex);
        Assert.All(pad.Controls.Where(c => c.Kind == ControlKind.Axis), c => Assert.True(c.Supported));
    }

    [Fact]
    public void Buttons_MatchFirmwareProfile()
    {
        Assert.Equal((5, (byte)16), (Btn(1).ByteIndex, Btn(1).Mask));  // val_autofire = 16
        Assert.Equal((5, (byte)32), (Btn(2).ByteIndex, Btn(2).Mask));  // val_up_alt   = 32
        Assert.Equal((5, (byte)64), (Btn(3).ByteIndex, Btn(3).Mask));  // val_fire1    = 64
        Assert.Equal((5, (byte)128), (Btn(4).ByteIndex, Btn(4).Mask)); // val_fire2    = 128
        Assert.Equal((6, (byte)2), (Btn(6).ByteIndex, Btn(6).Mask));   // val_fire3 = 2 on byte 6
        Assert.Equal(12, Pad().Controls.Count(c => c.Kind == ControlKind.Button));
    }

    [Fact]
    public void Hat_InLowNibbleOfButtonsByte_NoFalseWarning()
    {
        var hat = Pad().Controls.Single(c => c.Kind == ControlKind.Hat);
        Assert.Equal((5, true), (hat.ByteIndex, hat.Supported));
        var p = ProfileBuilder.Build(MappingProposer.Propose(Pad()), 0x0810, 0x0001);
        var issues = ProfileValidator.Validate(p);
        Assert.DoesNotContain(issues, i => i.Code == "HAT_ON_BUTTON_BYTE");
        Assert.Empty(issues);
    }

    [Fact]
    public void Hat_NotDisturbedByButtonsInHighNibble()
    {
        var p = ProfileBuilder.Build(MappingProposer.Propose(Pad()), 0x0810, 0x0001);
        var e = new UsbToC64.Core.Emulation.DynamicEngine(p);
        var report = new byte[] { 1, 128, 128, 128, 128, 0x02 | 0x10 | 0x40, 0, 0 }; // hat E + btn1 + btn3
        var s = e.Process(report)!.Value;
        Assert.True(s.Right && s.Fire1 && s.Fire2);   // btn1 = FIRE 1, btn3 = FIRE 2 in the proposal
        Assert.False(s.Up || s.Down || s.Left);
    }
}
