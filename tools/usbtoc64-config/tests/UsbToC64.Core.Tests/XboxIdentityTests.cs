using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using UsbToC64.Core.XInput;
using UsbToC64.Input;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Xbox One S seen by Windows as 045E:02FF (virtual), real USB device 045E:02EA (real case).</summary>
public class XboxIdentityTests
{
    [Fact]
    public void VirtualGipDevice_UsesRealUsbIds()
    {
        var usb = new[] { new UsbDeviceInfo(0x045E, 0x02EA, "xboxgip", @"USB\VID_045E&PID_02EA\3039363433") };
        var (kind, vid, pid, note) = PadDevices.ResolveXInput(0x045E, 0x02FF, usb);
        Assert.Equal(XInputKind.Gip, kind);
        Assert.Equal((0x045E, 0x02EA), ((int)vid, (int)pid));
        Assert.NotNull(note);
    }

    [Theory]
    [InlineData("dc1-controller")]
    [InlineData("")]           // driver name not readable: known PID 02EA
    public void VirtualGipDevice_OtherDriverNames(string service)
    {
        var usb = new[] { new UsbDeviceInfo(0x045E, 0x02EA, service, @"USB\VID_045E&PID_02EA\1") };
        var (kind, _, pid, _) = PadDevices.ResolveXInput(0x045E, 0x02FF, usb);
        Assert.Equal(XInputKind.Gip, kind);
        Assert.Equal(0x02EA, pid);
    }

    [Fact]
    public void VirtualGipDevice_NotFound_ListsMicrosoftDevices()
    {
        var usb = new[] { new UsbDeviceInfo(0x045E, 0x0999, "foo", @"USB\VID_045E&PID_0999\1") };
        var (_, _, pid, note) = PadDevices.ResolveXInput(0x045E, 0x02FF, usb);
        Assert.Equal(0x02FF, pid);
        Assert.Contains("045E:0999", note);
    }

    [Fact]
    public void Xbox360Clone_StaysXbox360()
    {
        var usb = new[]
        {
            new UsbDeviceInfo(0x045E, 0x028E, "xusb22", @"USB\VID_045E&PID_028E\1"),
            new UsbDeviceInfo(0x045E, 0x02EA, "xboxgip", @"USB\VID_045E&PID_02EA\2"),
        };
        var (kind, vid, pid, _) = PadDevices.ResolveXInput(0x045E, 0x028E, usb);
        Assert.Equal(XInputKind.Xbox360, kind);
        Assert.Equal(0x028E, pid);
    }

    [Fact]
    public void ProfileWithVirtualId_IsAnError()
    {
        // The profile written in the first Xbox One S test.
        var p = new JmProfile { Vid = 1118, Pid = 767 };
        p.Rules.Add(new JmRule(3, 16, JmOp.BitAny, JmFunc.Fire1));
        Assert.Contains(ProfileValidator.Validate(p), i => i.Code == "VIRTUAL_XBOX_ID");
    }
}
