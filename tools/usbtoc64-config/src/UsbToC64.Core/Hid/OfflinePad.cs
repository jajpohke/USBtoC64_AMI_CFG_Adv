using System.Text.Json;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Core.XInput;

namespace UsbToC64.Core.Hid;

/// <summary>
/// What the program remembers about a pad it has seen (the "card"): enough to describe its controls
/// again when the pad is not connected, e.g. to edit a drawer offline.
/// </summary>
public sealed record PadCard(ushort Vid, ushort Pid, string Name, XInputKind? XInput, string? DescriptorHex, DateTime Seen)
{
    /// <summary>Controls of the pad, as when it was connected.</summary>
    public PadDescription? Describe()
    {
        if (XInput != null) return XInputLayout.Describe(XInput.Value);
        if (string.IsNullOrEmpty(DescriptorHex)) return null;
        try { return ControlClassifier.Classify(Convert.FromHexString(DescriptorHex)); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException) { return null; }
    }
}

/// <summary>Cards of the pads seen by this PC, one JSON file per VID/PID in a folder.</summary>
public sealed class PadCatalog
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public PadCatalog(string directory) => _dir = directory;

    private string FileFor(ushort vid, ushort pid) => Path.Combine(_dir, $"{vid:X4}_{pid:X4}.json");

    public void Save(PadCard card)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FileFor(card.Vid, card.Pid), JsonSerializer.Serialize(card, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not remembered */ }
    }

    public PadCard? Find(ushort vid, ushort pid)
    {
        try
        {
            var f = FileFor(vid, pid);
            return File.Exists(f) ? JsonSerializer.Deserialize<PadCard>(File.ReadAllText(f)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}

/// <summary>Where the controls of an offline pad come from.</summary>
public enum OfflineSource
{
    /// <summary>The card saved when the pad was connected to this PC: all controls, real names.</summary>
    Card,
    /// <summary>A known Xbox pad: fixed layout.</summary>
    KnownXbox,
    /// <summary>Only the controls used by the stored rules, with technical names.</summary>
    Rules,
}

public static class OfflinePad
{
    /// <summary>
    /// Controls for editing a stored profile without the pad: the card, else a known Xbox layout,
    /// else controls rebuilt from the rules themselves.
    /// </summary>
    public static (PadDescription Pad, OfflineSource Source) Describe(JmProfile profile, PadCard? card)
    {
        if (card?.Describe() is { } fromCard) return (fromCard, OfflineSource.Card);
        if (XInputLayout.IsKnownXboxOne(profile.Vid, profile.Pid)) return (XInputLayout.DescribeGip(), OfflineSource.KnownXbox);
        if (XInputLayout.IsKnownXbox360Wired(profile.Vid, profile.Pid)) return (XInputLayout.Describe(), OfflineSource.KnownXbox);
        return (FromRules(profile), OfflineSource.Rules);
    }

    /// <summary>One control per button bit, hat byte and axis used by the rules.</summary>
    public static PadDescription FromRules(JmProfile profile)
    {
        var controls = new List<Control>();
        var seen = new HashSet<string>();

        void Add(Control c)
        {
            if (seen.Add(c.Id)) controls.Add(c);
        }

        foreach (var g in profile.Rules.Where(r => r.Op == JmOp.Hat).GroupBy(r => r.Index))
        {
            var values = g.Select(r => (int)r.Value).ToList();
            int hatBase = values.Contains(8) ? 1 : 0;
            bool four = values.All(v => v - hatBase is >= 0 and < 4)
                        && g.All(r => r.Func == new[] { JmFunc.Up, JmFunc.Right, JmFunc.Down, JmFunc.Left }[r.Value - hatBase]);
            Add(new Control
            {
                Id = $"hat{g.Key}", Name = L.F("offline.hat", g.Key), Kind = ControlKind.Hat,
                ByteIndex = g.Key, Mask = 0x0F, HatBase = hatBase, HatPositions = four ? 4 : 8,
            });
        }

        foreach (var r in profile.Rules)
        {
            if (FirmwareLimits.IsSigned(r.Op))
            {
                // ProfileBuilder: Y with "S>" = Up means "up is positive" (XInput).
                bool upPositive = profile.Rules.Any(o => o.Index == r.Index && o.Op == JmOp.SignedGreater && o.Func == JmFunc.Up);
                Add(new Control
                {
                    Id = $"sax{r.Index}", Name = L.F("offline.axis", r.Index), Kind = ControlKind.Axis,
                    ByteIndex = r.Index, Mask = 0xFF, Signed = true, UpIsPositive = upPositive,
                });
                continue;
            }
            bool singleBit = r.Value != 0 && (r.Value & (r.Value - 1)) == 0;
            if (r.Op == JmOp.BitAny || (r.Op == JmOp.Eq && singleBit))
            {
                // A BIT rule on several bits stays a single "button" on that mask.
                Add(new Control
                {
                    Id = $"b{r.Index}_{r.Value:X2}", Name = L.F("offline.button", r.Index, r.Value), Kind = ControlKind.Button,
                    ByteIndex = r.Index, Mask = r.Value,
                });
            }
        }

        // An index in both lists (a mistake the validator reports) gets two controls, so nothing is lost.
        foreach (var idx in profile.AnalogX)
            Add(new Control { Id = $"ax{idx}", Name = L.F("offline.axis", idx), Kind = ControlKind.Axis, ByteIndex = idx, Mask = 0xFF });
        foreach (var idx in profile.AnalogY)
            Add(new Control
            {
                Id = profile.AnalogX.Contains(idx) ? $"ay{idx}" : $"ax{idx}", Name = L.F("offline.axis", idx),
                Kind = ControlKind.Axis, ByteIndex = idx, Mask = 0xFF,
            });

        var d = new PadDescription
        {
            Controls = controls.OrderBy(c => c.ByteIndex).ThenBy(c => c.Kind).ThenBy(c => c.Mask).ToList(),
            UsesReportIds = false,
        };
        return d;
    }
}
