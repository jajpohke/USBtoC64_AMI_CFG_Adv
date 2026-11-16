using System.Text.Json;

namespace UsbToC64.Core.Device;

/// <summary>Mouse settings (mouse_get / mouse_save), same fields as the web page mouse.html.</summary>
public sealed record MouseSettings(int AmigaSpeed, int C64Speed, bool Wheel, bool Pal)
{
    public const int MinSpeed = 1, MaxSpeed = 5, DefaultSpeed = 3;
    public static readonly MouseSettings Default = new(DefaultSpeed, DefaultSpeed, false, true);
}

/// <summary>One LED colour slot of led_get / led_save.</summary>
public sealed record LedSlot(string Key, string Label);

/// <summary>LED settings (led_get / led_save), same fields as the web page led.html.</summary>
public sealed record LedSettings(IReadOnlyDictionary<string, string> Colors, int Format, bool Multicolor)
{
    public static readonly LedSlot[] Slots =
    {
        new("idle_c64", "Riposo (modalità C64)"),
        new("idle_amiga", "Riposo (modalità Amiga)"),
        new("dir_glowing", "Direzioni"),
        new("btn_fire1", "Fire 1"),
        new("btn_fire2", "Fire 2"),
        new("btn_fire3", "Fire 3"),
        new("btn_alt", "Su / salto"),
        new("btn_auto", "Autofire"),
    };

    /// <summary>The only colours the firmware stores (anything else becomes black).</summary>
    public static readonly (string Hex, string Name)[] Palette =
    {
        ("#FFFFFF", "Bianco"), ("#FF0000", "Rosso"), ("#FF9900", "Arancione"), ("#FFFF00", "Giallo"),
        ("#00FF00", "Verde"), ("#0000FF", "Blu"), ("#00FFFF", "Ciano"), ("#FF00FF", "Viola"),
        ("#FF99CC", "Rosa"), ("#000000", "Spento"),
    };

    /// <summary>Firmware defaults (as shown by the web page).</summary>
    public static readonly LedSettings Default = new(new Dictionary<string, string>
    {
        ["idle_c64"] = "#FF9900", ["idle_amiga"] = "#FFFFFF", ["dir_glowing"] = "#FF00FF", ["btn_fire1"] = "#00FF00",
        ["btn_fire2"] = "#FF0000", ["btn_fire3"] = "#00FFFF", ["btn_alt"] = "#0000FF", ["btn_auto"] = "#FFFF00",
    }, 0, true);
}

/// <summary>Which settings banks are saved in the adapter memory (mem_status).</summary>
public sealed record MemoryStatus(bool Mouse, bool Pad, bool Led, bool DevMode, bool Invert);

public static class AdapterSettingsCommands
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static JsonDocument Require(AdapterSession s, string json) =>
        s.RequestJson(json, Timeout) ?? throw new TimeoutException($"Nessuna risposta dall'adattatore a {json}");

    private static void RequireOk(AdapterSession s, string json)
    {
        using var doc = Require(s, json);
        var st = doc.RootElement.TryGetProperty("status", out var v) ? v.GetString() : null;
        if (st != "ok") throw new InvalidOperationException($"L'adattatore ha risposto: {doc.RootElement.GetRawText()}");
    }

    private static int Int(JsonElement r, string name, int def) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;

    private static bool Bool(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetInt32() != 0));

    // ---- mouse

    public static MouseSettings GetMouse(this AdapterSession s)
    {
        using var doc = Require(s, "{\"cmd\":\"mouse_get\"}");
        var r = doc.RootElement;
        return new MouseSettings(
            Math.Clamp(Int(r, "amiga_speed", 3), MouseSettings.MinSpeed, MouseSettings.MaxSpeed),
            Math.Clamp(Int(r, "c64_speed", 3), MouseSettings.MinSpeed, MouseSettings.MaxSpeed),
            Int(r, "enable_wheel", 0) != 0,
            Int(r, "pal_timing", 1) != 0);
    }

    public static string MouseJson(MouseSettings m) =>
        $"{{\"cmd\":\"mouse_save\",\"amiga_speed\":{m.AmigaSpeed},\"c64_speed\":{m.C64Speed},\"enable_wheel\":{(m.Wheel ? 1 : 0)},\"pal_timing\":{(m.Pal ? 1 : 0)}}}";

    public static void SaveMouse(this AdapterSession s, MouseSettings m) => RequireOk(s, MouseJson(m));

    // ---- LED

    public static LedSettings GetLed(this AdapterSession s)
    {
        using var doc = Require(s, "{\"cmd\":\"led_get\"}");
        var r = doc.RootElement;
        var colors = new Dictionary<string, string>();
        foreach (var slot in LedSettings.Slots)
            colors[slot.Key] = r.TryGetProperty(slot.Key, out var v) && v.GetString() is { } hex
                ? hex.ToUpperInvariant()
                : LedSettings.Default.Colors[slot.Key];
        return new LedSettings(colors, Int(r, "led_fmt", 0), Int(r, "multicolor", 1) != 0);
    }

    public static string LedJson(LedSettings l)
    {
        var parts = new List<string> { "\"cmd\":\"led_save\"" };
        foreach (var slot in LedSettings.Slots) parts.Add($"\"{slot.Key}\":\"{l.Colors[slot.Key]}\"");
        parts.Add($"\"led_fmt\":{l.Format}");
        parts.Add($"\"multicolor\":{(l.Multicolor ? 1 : 0)}");
        return "{" + string.Join(",", parts) + "}";
    }

    public static void SaveLed(this AdapterSession s, LedSettings l) => RequireOk(s, LedJson(l));

    /// <summary>Back to the default colours. The adapter restarts.</summary>
    public static void ResetLed(this AdapterSession s)
    {
        // ok is printed 200 ms before the restart: on Windows the reply may be lost with the port.
        try { s.RequestJson("{\"cmd\":\"led_reset\"}", Timeout)?.Dispose(); }
        catch (IOException) { /* port gone: the adapter is restarting */ }
    }

    // ---- settings / memory

    public static MemoryStatus GetMemoryStatus(this AdapterSession s)
    {
        using var doc = Require(s, "{\"cmd\":\"mem_status\"}");
        var r = doc.RootElement;
        return new MemoryStatus(Bool(r, "mouse"), Bool(r, "pad"), Bool(r, "led"), Bool(r, "dev"), Bool(r, "inv"));
    }

    public static void ToggleInvertSwitch(this AdapterSession s) => RequireOk(s, "{\"cmd\":\"invert_toggle\"}");
    public static void ToggleDevAmiga(this AdapterSession s) => RequireOk(s, "{\"cmd\":\"override_toggle\"}");
    public static void ToggleDevMode(this AdapterSession s) => RequireOk(s, "{\"cmd\":\"devmode_toggle\"}");

    /// <summary>target: mouse, pad, led, dev, inv.</summary>
    public static void ClearMemory(this AdapterSession s, string target) =>
        RequireOk(s, $"{{\"cmd\":\"clear_mem\",\"target\":\"{target}\"}}");

    /// <summary>The adapter answers ok and restarts: the reply may be lost when the port goes away.</summary>
    public static void Reboot(this AdapterSession s)
    {
        try { s.RequestJson("{\"cmd\":\"reboot_cmd\"}", Timeout)?.Dispose(); }
        catch (IOException) { /* port gone: the adapter is restarting */ }
    }

    /// <summary>Erases ALL settings and restarts.</summary>
    public static void FactoryReset(this AdapterSession s)
    {
        try { s.RequestJson("{\"cmd\":\"factory_reset\"}", Timeout)?.Dispose(); }
        catch (IOException) { /* port gone: the adapter is restarting */ }
    }
}
