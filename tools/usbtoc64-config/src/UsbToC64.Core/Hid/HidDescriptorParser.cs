namespace UsbToC64.Core.Hid;

/// <summary>One element of an Input main item (one value of a variable field, or one array slot).</summary>
public sealed record HidInputElement(
    byte ReportId,
    // Bit offset inside the report data (report id byte excluded).
    int BitOffset,
    int BitSize,
    ushort UsagePage,
    ushort Usage,
    int LogicalMin,
    int LogicalMax,
    bool IsConstant,
    bool IsVariable,
    bool IsRelative,
    bool HasNullState,
    // Usage of the enclosing top-level application collection (0x04 joystick, 0x05 gamepad...).
    ushort ApplicationUsage);

public sealed class HidReportLayout
{
    public required IReadOnlyList<HidInputElement> Elements { get; init; }
    // True if the descriptor declares report ids: the raw report then starts with the id byte.
    public required bool UsesReportIds { get; init; }
    // Input report ids that carry at least one non-constant element.
    public required IReadOnlyList<byte> InputReportIds { get; init; }
    // Input report size in bytes per report id (data only, id byte excluded).
    public required IReadOnlyDictionary<byte, int> InputReportBytes { get; init; }
}

/// <summary>
/// Minimal HID report descriptor parser (HID 1.11, short items), enough to locate
/// the Input fields of game controllers. Long items are skipped.
/// </summary>
public static class HidDescriptorParser
{
    private sealed class Globals
    {
        public ushort UsagePage;
        public int LogicalMin, LogicalMax;
        public int ReportSize, ReportCount;
        public byte ReportId;
        public Globals Clone() => (Globals)MemberwiseClone();
    }

    public static HidReportLayout Parse(ReadOnlySpan<byte> d)
    {
        var g = new Globals();
        var stack = new Stack<Globals>();
        var usages = new List<uint>();
        uint? usageMin = null, usageMax = null;
        var collectionDepth = 0;
        ushort appUsage = 0;
        var usesIds = false;
        var bitPos = new Dictionary<byte, int>();
        var elements = new List<HidInputElement>();

        int i = 0;
        while (i < d.Length)
        {
            byte prefix = d[i];
            if (prefix == 0xFE) // long item
            {
                if (i + 1 >= d.Length) break;
                i += 3 + d[i + 1];
                continue;
            }
            int size = (prefix & 0x03) == 3 ? 4 : prefix & 0x03;
            int type = (prefix >> 2) & 0x03;
            int tag = (prefix >> 4) & 0x0F;
            if (i + 1 + size > d.Length) break;
            var data = d.Slice(i + 1, size);
            uint u = 0;
            for (int k = 0; k < size; k++) u |= (uint)data[k] << (8 * k);
            int s = size switch
            {
                1 => (sbyte)data[0],
                2 => (short)u,
                4 => (int)u,
                _ => 0,
            };
            i += 1 + size;

            switch (type)
            {
                case 0: // Main
                    switch (tag)
                    {
                        case 0x8: // Input
                            EmitInput(u, g, usages, usageMin, usageMax, appUsage, bitPos, elements);
                            break;
                        case 0x9: // Output
                        case 0xB: // Feature
                            break; // not relevant, and they do not move the input bit position
                        case 0xA: // Collection
                            if (collectionDepth == 0 && (u & 0xFF) == 0x01)
                                appUsage = usages.Count > 0 ? (ushort)(usages[0] & 0xFFFF) : (ushort)0;
                            collectionDepth++;
                            break;
                        case 0xC: // End collection
                            if (collectionDepth > 0) collectionDepth--;
                            break;
                    }
                    usages.Clear(); usageMin = usageMax = null; // locals are reset after each main item
                    break;

                case 1: // Global
                    switch (tag)
                    {
                        case 0x0: g.UsagePage = (ushort)u; break;
                        case 0x1: g.LogicalMin = s; break;
                        case 0x2: g.LogicalMax = s; break;
                        case 0x7: g.ReportSize = (int)u; break;
                        case 0x8: g.ReportId = (byte)u; usesIds = true; break;
                        case 0x9: g.ReportCount = (int)u; break;
                        case 0xA: stack.Push(g.Clone()); break;
                        case 0xB: if (stack.Count > 0) g = stack.Pop(); break;
                    }
                    break;

                case 2: // Local
                    uint full = size == 4 ? u : ((uint)g.UsagePage << 16) | u;
                    switch (tag)
                    {
                        case 0x0: usages.Add(full); break;
                        case 0x1: usageMin = full; break;
                        case 0x2: usageMax = full; break;
                    }
                    break;
            }
        }

        var ids = elements.Where(e => !e.IsConstant).Select(e => e.ReportId).Distinct().OrderBy(x => x).ToList();
        var bytes = bitPos.ToDictionary(kv => kv.Key, kv => (kv.Value + 7) / 8);
        return new HidReportLayout
        {
            Elements = elements,
            UsesReportIds = usesIds,
            InputReportIds = ids,
            InputReportBytes = bytes,
        };
    }

    private static void EmitInput(uint flags, Globals g, List<uint> usages, uint? usageMin, uint? usageMax,
        ushort appUsage, Dictionary<byte, int> bitPos, List<HidInputElement> elements)
    {
        bool constant = (flags & 0x01) != 0;
        bool variable = (flags & 0x02) != 0;
        bool relative = (flags & 0x04) != 0;
        bool nullState = (flags & 0x40) != 0;

        // Some descriptors write e.g. "25 FF" for 255: treat an inverted range as unsigned.
        int lmin = g.LogicalMin, lmax = g.LogicalMax;
        if (lmax < lmin && g.ReportSize > 0 && g.ReportSize < 32)
            lmax = (int)(((long)lmax) & ((1L << g.ReportSize) - 1));

        var list = new List<uint>(usages);
        if (usageMin.HasValue && usageMax.HasValue && usageMax >= usageMin && usageMax - usageMin < 1024)
            for (uint x = usageMin.Value; x <= usageMax.Value; x++) list.Add(x);

        bitPos.TryGetValue(g.ReportId, out int pos);
        for (int n = 0; n < g.ReportCount; n++)
        {
            uint usage = list.Count == 0 ? 0 : list[Math.Min(n, list.Count - 1)];
            if (!variable && list.Count > 0) usage = list[0]; // array: usage = first of the range
            elements.Add(new HidInputElement(
                g.ReportId, pos + n * g.ReportSize, g.ReportSize,
                (ushort)(usage >> 16), (ushort)(usage & 0xFFFF),
                lmin, lmax, constant, variable, relative, nullState, appUsage));
        }
        bitPos[g.ReportId] = pos + g.ReportCount * g.ReportSize;
    }
}
