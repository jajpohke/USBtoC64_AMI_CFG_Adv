using UsbToC64.Core.Device;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Mouse / LED / memory commands, same JSON as the web pages.</summary>
public class SettingsTests
{
    private static AdapterConnection Connect(FakeAdapter a) => AdapterConnection.Connect(a, null, _ => { });

    [Fact]
    public void Mouse_ReadAndSave()
    {
        var a = new FakeAdapter();
        using var c = Connect(a);
        var m = c.Session.GetMouse();
        Assert.Equal(new MouseSettings(4, 2, true, false), m);
        c.Session.SaveMouse(m with { AmigaSpeed = 5 });
        Assert.Equal("{\"cmd\":\"mouse_save\",\"amiga_speed\":5,\"c64_speed\":2,\"enable_wheel\":1,\"pal_timing\":0}", a.LastSettings);
    }

    [Fact]
    public void Led_ReadAndSave_SameKeysAsWebPage()
    {
        var a = new FakeAdapter();
        using var c = Connect(a);
        var l = c.Session.GetLed();
        Assert.Equal(1, l.Format);
        Assert.False(l.Multicolor);
        Assert.Equal("#000000", l.Colors["btn_auto"]);
        c.Session.SaveLed(l);
        Assert.Equal("{\"cmd\":\"led_save\",\"idle_c64\":\"#FF9900\",\"idle_amiga\":\"#FFFFFF\",\"dir_glowing\":\"#FF00FF\"," +
                     "\"btn_fire1\":\"#00FF00\",\"btn_fire2\":\"#FF0000\",\"btn_fire3\":\"#00FFFF\",\"btn_alt\":\"#0000FF\",\"btn_auto\":\"#000000\"," +
                     "\"led_fmt\":1,\"multicolor\":0}", a.LastSettings);
    }

    [Fact]
    public void MemoryStatus_Read()
    {
        using var c = Connect(new FakeAdapter());
        Assert.Equal(new MemoryStatus(true, false, true, false, false), c.Session.GetMemoryStatus());
    }

    [Fact]
    public void LedDefaults_UseOnlyPaletteColours()
    {
        var palette = LedSettings.Palette.Select(p => p.Hex).ToHashSet();
        Assert.All(LedSettings.Default.Colors.Values, h => Assert.Contains(h, palette));
        Assert.Equal(LedSettings.Slots.Length, LedSettings.Default.Colors.Count);
    }
}
