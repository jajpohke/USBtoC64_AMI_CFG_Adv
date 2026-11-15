using UsbToC64.Core.Device;
using UsbToC64.Core.Emulation;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.XInput;
using Xunit;

namespace UsbToC64.Core.Tests;

public class SignedStickTests
{
    private static JmProfile Xbox360() =>
        ProfileBuilder.Build(MappingProposer.Propose(XInputLayout.Describe(XInputKind.Xbox360)), 0x045E, 0x028E);

    private static C64State Run(JmProfile p, short lx, short ly) =>
        new DynamicEngine(p).Process(XInputLayout.BuildNativeReport(0, 0, 0, lx, ly, 0, 0))!.Value;

    [Fact]
    public void Stick_DeadZoneAndDirections()
    {
        var p = Xbox360();
        Assert.Equal(default, Run(p, 0, 0));
        Assert.Equal(default, Run(p, 10000, -10000));           // ~30 %: inside the dead zone
        var s = Run(p, -30000, 30000);                            // left + up
        Assert.True(s.Left && s.Up);
        Assert.False(s.Right || s.Down);
        s = Run(p, 32767, -32768);                                // right + down, extremes
        Assert.True(s.Right && s.Down);
    }

    [Fact]
    public void Gip_Stick_Works()
    {
        var p = ProfileBuilder.Build(MappingProposer.Propose(XInputLayout.Describe(XInputKind.Gip)), 0x045E, 0x02EA);
        var s = new DynamicEngine(p).Process(XInputLayout.BuildGipReport(0, 0, 0, 0, -32000, 0, 0))!.Value;
        Assert.True(s.Down);
        Assert.False(s.Up || s.Left || s.Right);
    }

    [Fact]
    public void Importer_RecognisesSignedRules()
    {
        var pad = XInputLayout.Describe(XInputKind.Xbox360);
        var r = MappingImporter.FromProfile(pad, Xbox360());
        Assert.Empty(r.UnmatchedRules);
        Assert.Equal(PadAction.AxisX, r.Mapping.Get(pad.Controls.Single(c => c.Id == "axis_lx")));
        Assert.Equal(PadAction.AxisY, r.Mapping.Get(pad.Controls.Single(c => c.Id == "axis_ly")));
    }

    [Fact]
    public void Dumppad_RoundTripsSignedRules()
    {
        var p = Xbox360();
        var q = DumppadParser.Parse(FakeAdapter.Dump(p))!;
        Assert.Empty(ProfileComparer.Compare(p, q));
    }

    [Fact]
    public void Writer_RefusesSignedRules_OnOlderFirmware()
    {
        var a = new FakeAdapter { Version = "1.2.2" };
        var r = new AdapterWriter(a, null, _ => { }) { BootDelay = TimeSpan.Zero }.WriteAndVerify(Xbox360());
        Assert.False(r.Saved);
        Assert.Contains("1.2.3", r.Message);
        Assert.Null(a.Stored);

        var b = new FakeAdapter { Version = "1.2.3" };
        Assert.True(new AdapterWriter(b, null, _ => { }) { BootDelay = TimeSpan.Zero }.WriteAndVerify(Xbox360()).Verified);
    }
}
