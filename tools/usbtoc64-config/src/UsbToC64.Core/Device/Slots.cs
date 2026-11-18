using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;

namespace UsbToC64.Core.Device;

/// <summary>
/// A drawer of firmware 1.3.0: one pad profile (VID/PID) with its label.
/// An empty label means "saved by the web configurator" and is shown as <see cref="SlotLabels.WebConfigured"/>.
/// </summary>
public sealed record SlotInfo(int Slot, ushort Vid, ushort Pid, string Label, int RuleCount, bool Analog, uint Seq)
{
    /// <summary>1-based drawer number, as shown to the user.</summary>
    public int Number => Slot + 1;
    public bool IsWebConfigured => string.IsNullOrEmpty(Label);
    public string DisplayLabel => IsWebConfigured ? SlotLabels.WebConfigured : Label;
    public string Ids => $"{Vid:X4}:{Pid:X4}";
}

/// <summary>A drawer with its rules (get_jm).</summary>
public sealed record SlotContent(SlotInfo Info, JmProfile Profile);

public static class SlotLabels
{
    /// <summary>Shown for drawers without a label (saved by the web configurator). Not translated.</summary>
    public const string WebConfigured = "WebConfigured";

    /// <summary>
    /// Label as the firmware stores it: control characters removed, spaces trimmed, at most
    /// <see cref="FirmwareLimits.LabelMaxBytes"/> UTF-8 bytes without cutting a character.
    /// </summary>
    public static string Clean(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var sb = new StringBuilder();
        int bytes = 0;
        foreach (var rune in label.Trim().EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            int n = rune.Utf8SequenceLength;
            if (bytes + n > FirmwareLimits.LabelMaxBytes) break;
            sb.Append(rune.ToString());
            bytes += n;
        }
        return sb.ToString().Trim();
    }

    /// <summary>UTF-8 bytes of a label (the firmware limit is in bytes, not characters).</summary>
    public static int ByteCount(string label) => Encoding.UTF8.GetByteCount(label);

    private static readonly Regex WindowsController = new(@"^Controller \((.+)\)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Automatic label from the name Windows gives the pad ("Controller (Xbox One For Windows)" → "Xbox One For Windows").
    /// Without a name: "Pad VID:PID".
    /// </summary>
    public static string Auto(string? deviceName, ushort vid, ushort pid)
    {
        var name = (deviceName ?? "").Trim();
        var m = WindowsController.Match(name);
        if (m.Success) name = m.Groups[1].Value.Trim();
        name = Clean(name);
        return name.Length > 0 ? name : $"Pad {vid:X4}:{pid:X4}";
    }

    /// <summary>
    /// The drawer a new save of this pad goes to, like the firmware: its own drawer, else the first free one,
    /// else the oldest.
    /// </summary>
    public static int Choose(IReadOnlyList<SlotInfo> used, ushort vid, ushort pid)
    {
        var own = used.FirstOrDefault(s => s.Vid == vid && s.Pid == pid);
        if (own != null) return own.Slot;
        for (int i = 0; i < FirmwareLimits.MaxSlots; i++)
            if (used.All(s => s.Slot != i)) return i;
        return used.OrderBy(s => s.Seq).First().Slot;
    }
}

/// <summary>Drawer commands of firmware 1.3.0 (proto 2).</summary>
public static class SlotCommands
{
    public static bool Supports(AdapterFirmware fw) =>
        !fw.IsOriginal && !(fw.Version < FirmwareVersion.Parse(FirmwareLimits.SlotsMinFirmware));

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static string Status(JsonElement root) =>
        root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name, int def = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;

    private static uint UInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetUInt32() : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>The drawers in use, ordered by number.</summary>
    public static List<SlotInfo> ListSlots(this AdapterSession session)
    {
        using var doc = session.RequestJson("{\"cmd\":\"list_jm\"}", Timeout)
                        ?? throw new TimeoutException(L.T("slot.noreply"));
        var root = doc.RootElement;
        if (Status(root) != "ok") throw new InvalidOperationException(L.F("proto.replyerror", root.GetRawText()));
        var list = new List<SlotInfo>();
        if (root.TryGetProperty("items", out var items))
            foreach (var o in items.EnumerateArray())
                list.Add(new SlotInfo(Int(o, "slot"), (ushort)Int(o, "vid"), (ushort)Int(o, "pid"), Str(o, "label"),
                    Int(o, "rules"), Int(o, "analog") != 0, UInt(o, "seq")));
        return list.OrderBy(s => s.Slot).ToList();
    }

    /// <summary>One drawer with its rules; null when empty.</summary>
    public static SlotContent? ReadSlot(this AdapterSession session, int slot)
    {
        using var doc = session.RequestJson($"{{\"cmd\":\"get_jm\",\"slot\":{slot}}}", Timeout)
                        ?? throw new TimeoutException(L.T("slot.noreply"));
        var root = doc.RootElement;
        string status = Status(root);
        if (status == "empty") return null;
        if (status != "ok") throw new InvalidOperationException(L.F("proto.replyerror", root.GetRawText()));
        var profile = SaveJmJson.Parse(root.GetRawText());
        var info = new SlotInfo(Int(root, "slot", slot), profile.Vid, profile.Pid, Str(root, "label"),
            profile.Rules.Count, profile.UseAnalog, UInt(root, "seq"));
        return new SlotContent(info, profile);
    }

    /// <summary>All the drawers in use, with their rules.</summary>
    public static List<SlotContent> ReadAllSlots(this AdapterSession session) =>
        session.ListSlots().Select(s => session.ReadSlot(s.Slot)).OfType<SlotContent>().ToList();

    public static void RenameSlot(this AdapterSession session, int slot, string label)
    {
        string json = $"{{\"cmd\":\"label_jm\",\"slot\":{slot},\"label\":{JsonSerializer.Serialize(SlotLabels.Clean(label))}}}";
        Expect(session, json);
    }

    public static void DeleteSlot(this AdapterSession session, int slot) =>
        Expect(session, $"{{\"cmd\":\"del_jm\",\"slot\":{slot}}}");

    private static void Expect(AdapterSession session, string json)
    {
        using var doc = session.RequestJson(json, Timeout) ?? throw new TimeoutException(L.T("slot.noreply"));
        if (Status(doc.RootElement) != "ok")
            throw new InvalidOperationException(L.F("proto.rejected", doc.RootElement.GetRawText()));
    }
}
