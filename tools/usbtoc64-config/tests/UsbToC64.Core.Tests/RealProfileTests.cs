using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Profiles exported by the web configurators and analysed by hand (see DESIGN.md §1).</summary>
public class RealProfileTests
{
    private static ImportedHeader Load(string name) =>
        HeaderImporter.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    private static HashSet<string> Codes(ImportedHeader h) =>
        ProfileValidator.Validate(h.Profile, h).Select(i => i.Code).ToHashSet();

    [Fact]
    public void File25_X360_SyntheticIndexes_And_DpadAsAxis()
    {
        var h = Load("JoystickMapping_25.h");
        Assert.Equal(HeaderGenerator.SmartWebConfigurator, h.Generator);
        Assert.Equal((1118, 654), ((int)h.Profile.Vid, (int)h.Profile.Pid));
        Assert.Equal(10, h.Profile.Rules.Count);
        Assert.Equal(10, h.WebMeta.Count);

        var codes = Codes(h);
        Assert.Contains("SAME_CONDITION", codes);          // DOWN + RIGHT on Byte[11] == 255
        Assert.Contains("AXIS_CAPTURED_AS_VALUE", codes);  // P5
        Assert.Contains("HAT_ON_BUTTON_BYTE", codes);      // UP as HAT on the buttons byte
        Assert.Contains("XINPUT_SYNTHETIC_INDEXES", codes); // P8
    }

    [Fact]
    public void File24_DpadIndexOnButtonsByte_And_SameAxisXY()
    {
        var h = Load("JoystickMapping_24.h");
        Assert.Equal(10, h.Profile.DpadIndex);
        Assert.True(h.HeaderUsesDpadIndex);
        Assert.Equal(new byte[] { 11 }, h.Profile.AnalogX);
        Assert.Equal(new byte[] { 11 }, h.Profile.AnalogY);

        var codes = Codes(h);
        Assert.Contains("HEADER_DPAD_INDEX", codes);   // P7: all buttons ignored in the .h
        Assert.Contains("SAME_AXIS_XY", codes);
        Assert.Contains("HAT_ON_BUTTON_BYTE", codes);  // B captured as 10:4 like X
        Assert.Contains("XINPUT_SYNTHETIC_INDEXES", codes);
    }

    [Fact]
    public void File3_1_Legacy_Empty()
    {
        var h = Load("JoystickMapping_3_1.h");
        Assert.Equal(HeaderGenerator.LegacyWebHid, h.Generator);
        Assert.Equal((1411, 8288), ((int)h.Profile.Vid, (int)h.Profile.Pid));
        Assert.True(h.Profile.IsEmpty);
        Assert.Contains("EMPTY", Codes(h));
        Assert.DoesNotContain("NO_VIDPID", Codes(h));
    }

    [Fact]
    public void File2_1_EmptyWebMeta_NoDevice()
    {
        var h = Load("JoystickMapping_2_1.h");
        Assert.Empty(h.WebMeta);
        var codes = Codes(h);
        Assert.Contains("EMPTY", codes);
        Assert.Contains("NO_VIDPID", codes);
    }

    [Fact]
    public void TestProfileFromSerialTerminal_IsValidJson()
    {
        // The line sent by hand during the save test (prova B).
        var p = SaveJmJson.Parse("{\"cmd\":\"save_jm\",\"vid\":1118,\"pid\":654,\"dpad_idx\":-1,\"use_analog\":0,\"analog_x\":[],\"analog_y\":[],\"rules\":[{\"i\":10,\"v\":1,\"op\":0,\"f\":8}]}");
        Assert.Single(p.Rules);
        Assert.Equal(new JmRule(10, 1, JmOp.Eq, JmFunc.Fire1), p.Rules[0]);
    }
}
