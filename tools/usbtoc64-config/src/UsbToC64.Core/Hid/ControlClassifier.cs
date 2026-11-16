using UsbToC64.Core.Text;

namespace UsbToC64.Core.Hid;

/// <summary>
/// Turns HID input elements into controls usable by the firmware, deciding automatically
/// what is directional (hat, D-pad usages, X/Y axes) and what is a button.
/// </summary>
public static class ControlClassifier
{
    private const ushort PageGenericDesktop = 0x01;
    private const ushort PageButton = 0x09;
    private const ushort UsageHat = 0x39;

    private static readonly Dictionary<ushort, string> AxisNames = new()
    {
        [0x30] = "X", [0x31] = "Y", [0x32] = "Z",
        [0x33] = "Rx", [0x34] = "Ry", [0x35] = "Rz",
        [0x36] = "Slider", [0x37] = "Dial", [0x38] = "Wheel",
    };

    private static readonly Dictionary<ushort, DpadDirection> DpadUsages = new()
    {
        [0x90] = DpadDirection.Up, [0x91] = DpadDirection.Down,
        [0x92] = DpadDirection.Right, [0x93] = DpadDirection.Left,
    };

    public static PadDescription Classify(ReadOnlySpan<byte> reportDescriptor) =>
        Classify(HidDescriptorParser.Parse(reportDescriptor));

    public static PadDescription Classify(HidReportLayout layout)
    {
        var controls = new List<Control>();
        var notes = new List<string>();

        // The firmware rules do not look at the report id: with several input reports
        // a rule could match bytes of the wrong report. Use the report with most controls.
        byte reportId = 0;
        if (layout.InputReportIds.Count > 0)
        {
            reportId = layout.InputReportIds
                .OrderByDescending(id => layout.Elements.Count(e => e.ReportId == id && !e.IsConstant && IsInteresting(e)))
                .First();
            if (layout.InputReportIds.Count > 1)
                notes.Add(L.F("hid.multireport", layout.InputReportIds.Count, string.Join(", ", layout.InputReportIds), reportId));
        }

        int offset = layout.UsesReportIds ? 1 : 0; // ESP32 raw report starts with the id byte when ids are used
        int buttonNo = 0;
        var axisSeen = new Dictionary<string, int>();
        int vendorFields = 0;

        foreach (var e in layout.Elements.Where(e => e.ReportId == reportId && !e.IsConstant))
        {
            if (e.UsagePage >= 0xFF00) { vendorFields++; continue; }

            if (e.UsagePage == PageButton && e.IsVariable && e.BitSize == 1)
            {
                buttonNo = e.Usage > 0 ? e.Usage : buttonNo + 1;
                controls.Add(BitControl($"btn{buttonNo}", L.F("hid.button", buttonNo), ControlKind.Button, e, offset));
                continue;
            }
            if (e.UsagePage == PageButton && !e.IsVariable)
            {
                notes.Add(L.T("hid.buttonarray"));
                continue;
            }
            if (e.UsagePage != PageGenericDesktop) continue;

            if (DpadUsages.TryGetValue(e.Usage, out var dir) && e.BitSize == 1)
            {
                controls.Add(BitControl($"dpad_{dir.ToString().ToLowerInvariant()}", $"D-pad {dir}", ControlKind.DpadButton, e, offset) with { Dpad = dir });
                continue;
            }
            if (e.Usage == UsageHat)
            {
                controls.Add(HatControl(e, offset, controls.Count(c => c.Kind == ControlKind.Hat)));
                continue;
            }
            if (AxisNames.TryGetValue(e.Usage, out var axisName) && !e.IsRelative)
            {
                axisSeen.TryGetValue(axisName, out int n);
                axisSeen[axisName] = n + 1;
                string id = n == 0 ? $"axis_{axisName.ToLowerInvariant()}" : $"axis_{axisName.ToLowerInvariant()}{n + 1}";
                string name = n == 0 ? L.F("hid.axis", axisName) : L.F("hid.axisn", axisName, n + 1);
                controls.Add(AxisControl(id, name, e, offset));
            }
        }

        if (vendorFields > 0)
            notes.Add(L.F("hid.vendor", vendorFields));

        var desc = new PadDescription { Controls = controls, UsesReportIds = layout.UsesReportIds };
        desc.Notes.AddRange(notes);
        return desc;
    }

    private static bool IsInteresting(HidInputElement e) =>
        e.UsagePage == PageButton ||
        (e.UsagePage == PageGenericDesktop && (e.Usage == UsageHat || AxisNames.ContainsKey(e.Usage) || DpadUsages.ContainsKey(e.Usage)));

    private static Control BitControl(string id, string name, ControlKind kind, HidInputElement e, int offset) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        ByteIndex = offset + e.BitOffset / 8,
        Mask = (byte)(1 << (e.BitOffset % 8)),
    };

    private static Control HatControl(HidInputElement e, int offset, int index)
    {
        int range = e.LogicalMax - e.LogicalMin + 1;
        int positions = range == 4 ? 4 : 8;
        string? note = null;
        bool ok = true;
        if (e.BitOffset % 8 != 0 || e.BitSize > 4)
        {
            ok = false;
            note = L.T("hid.hat.notlow");
        }
        else if (range != 4 && range != 8)
        {
            ok = false;
            note = L.F("hid.hat.positions", range);
        }
        else if (e.LogicalMin + positions - 1 > 0x0F || e.LogicalMin < 0)
        {
            ok = false;
            note = L.T("hid.hat.range");
        }
        return new Control
        {
            Id = index == 0 ? "hat" : $"hat{index + 1}",
            Name = index == 0 ? "D-pad (hat)" : $"Hat {index + 1}",
            Kind = ControlKind.Hat,
            ByteIndex = offset + e.BitOffset / 8,
            Mask = 0x0F,
            HatBase = e.LogicalMin,
            HatPositions = positions,
            Supported = ok,
            Note = note,
        };
    }

    private static Control AxisControl(string id, string name, HidInputElement e, int offset)
    {
        string? note = null;
        bool ok = true;
        bool signed = e.LogicalMin < 0;
        // The firmware compares one byte: use the top 8 bits of the field
        // (unsigned: analog thresholds 64/190; signed: S</S> rules, firmware 1.2.3+).
        int topBit = e.BitOffset + e.BitSize - 8;
        if (e.BitSize < 8)
        {
            ok = false;
            note = L.F("hid.axis.small", e.BitSize);
        }
        else if (topBit % 8 != 0)
        {
            ok = false;
            note = L.F("hid.axis.unaligned", e.BitSize);
        }
        else if (signed)
        {
            note = L.F("hid.axis.signed", Model.FirmwareLimits.SignedOpsMinFirmware);
        }
        else
        {
            long full = (1L << Math.Min(e.BitSize, 31)) - 1;
            if (e.LogicalMax < full * 3 / 4)
                note = L.F("hid.axis.range", e.LogicalMax, e.BitSize);
        }
        return new Control
        {
            Id = id,
            Name = name,
            Kind = ControlKind.Axis,
            ByteIndex = offset + Math.Max(topBit, e.BitOffset) / 8,
            Mask = 0xFF,
            AxisUsage = e.Usage,
            Signed = signed,
            Supported = ok,
            Note = note,
        };
    }
}
