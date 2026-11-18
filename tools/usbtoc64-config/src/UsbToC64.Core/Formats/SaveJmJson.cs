using System.Globalization;
using System.Text;
using System.Text.Json;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;

namespace UsbToC64.Core.Formats;

/// <summary>The single-line JSON accepted by the firmware command <c>save_jm</c>.</summary>
public static class SaveJmJson
{
    /// <summary>Serializes in the exact key order used by the web configurator. No trailing newline.</summary>
    public static string Serialize(JmProfile p) => Serialize(p, null, null);

    /// <summary>
    /// With a drawer and/or a label (firmware 1.3.0). Older firmware ignores the two extra keys.
    /// </summary>
    public static string Serialize(JmProfile p, int? slot, string? label)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"cmd\":\"save_jm\"");
        sb.Append(",\"vid\":").Append(p.Vid.ToString(inv));
        sb.Append(",\"pid\":").Append(p.Pid.ToString(inv));
        sb.Append(",\"dpad_idx\":").Append(p.DpadIndex.ToString(inv));
        sb.Append(",\"use_analog\":").Append(p.UseAnalog ? 1 : 0);
        sb.Append(",\"analog_x\":[").Append(string.Join(",", p.AnalogX)).Append(']');
        sb.Append(",\"analog_y\":[").Append(string.Join(",", p.AnalogY)).Append(']');
        sb.Append(",\"rules\":[");
        for (int i = 0; i < p.Rules.Count; i++)
        {
            var r = p.Rules[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"i\":").Append(r.Index)
              .Append(",\"v\":").Append(r.Value)
              .Append(",\"op\":").Append((int)r.Op)
              .Append(",\"f\":").Append((int)r.Func).Append('}');
        }
        sb.Append(']');
        if (slot != null) sb.Append(",\"slot\":").Append(slot.Value.ToString(inv));
        if (label != null) sb.Append(",\"label\":").Append(JsonSerializer.Serialize(label));
        sb.Append('}');
        return sb.ToString();
    }

    public static JmProfile Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("cmd", out var cmd) && cmd.GetString() != "save_jm")
            throw new FormatException(L.F("fmt.badcmd", cmd.GetString()));

        var p = new JmProfile
        {
            Vid = (ushort)GetInt(root, "vid", 0),
            Pid = (ushort)GetInt(root, "pid", 0),
            DpadIndex = (sbyte)GetInt(root, "dpad_idx", -1),
        };
        bool useAnalog = GetInt(root, "use_analog", 0) != 0;
        if (useAnalog)
        {
            // Like the firmware: the axes are read only when use_analog is set.
            if (root.TryGetProperty("analog_x", out var ax)) foreach (var v in ax.EnumerateArray()) p.AnalogX.Add((byte)v.GetInt32());
            if (root.TryGetProperty("analog_y", out var ay)) foreach (var v in ay.EnumerateArray()) p.AnalogY.Add((byte)v.GetInt32());
        }
        if (!root.TryGetProperty("rules", out var rules))
            throw new FormatException(L.T("fmt.norulesjson"));
        foreach (var r in rules.EnumerateArray())
        {
            p.Rules.Add(new JmRule(
                (byte)GetInt(r, "i", 0), (byte)GetInt(r, "v", 0),
                (JmOp)GetInt(r, "op", 0), (JmFunc)GetInt(r, "f", 0)));
        }
        return p;
    }

    private static int GetInt(JsonElement e, string name, int def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;

    /// <summary>
    /// Estimate of the ArduinoJson 6 memory needed to parse the command on the ESP32
    /// (16-byte slots on 32-bit, keys deduplicated, string values copied). Approximate.
    /// </summary>
    public static int EstimateArduinoJsonBytes(JmProfile p)
    {
        const int slot = 16;
        int slots = 10                                 // root members (slot and label included)
                    + p.AnalogX.Count + p.AnalogY.Count // array elements
                    + p.Rules.Count * (1 + 4);          // array element + 4 members each
        int strings = "save_jm".Length + 1 + FirmwareLimits.LabelMaxBytes + 1 + "slotlabel".Length + 2
                      + "cmd vid pid dpad_idx use_analog analog_x analog_y rules i v op f".Replace(" ", "").Length + 12;
        return slots * slot + strings;
    }
}
