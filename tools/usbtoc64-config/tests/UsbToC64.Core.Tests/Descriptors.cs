namespace UsbToC64.Core.Tests;

/// <summary>Hand-written report descriptors covering the common gamepad layouts.</summary>
internal static class Descriptors
{
    public static byte[] Hex(string s) =>
        s.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    /// <summary>
    /// Generic gamepad, no report id: X,Y 8 bit (bytes 0,1), hat 0..7 low nibble of byte 2,
    /// 12 buttons from bit 24 (byte 3 bit 0 .. byte 4 bit 3).
    /// </summary>
    public static readonly byte[] GenericGamepad = Hex(@"
        05 01 09 05 A1 01
        15 00 26 FF 00 75 08 95 02 09 30 09 31 81 02
        09 39 15 00 25 07 35 00 46 3B 01 65 14 75 04 95 01 81 42
        65 00 75 04 95 01 81 03
        05 09 19 01 29 0C 15 00 25 01 75 01 95 0C 81 02
        75 04 95 01 81 03
        C0");

    /// <summary>
    /// Report id 1, X/Y 16 bit unsigned (raw bytes 1-2 and 3-4), hat 1..8 in raw byte 5, 8 buttons in raw byte 6.
    /// </summary>
    public static readonly byte[] ReportId16Bit = Hex(@"
        05 01 09 04 A1 01 85 01
        05 01 09 30 09 31 15 00 27 FF FF 00 00 75 10 95 02 81 02
        09 39 15 01 25 08 75 04 95 01 81 42
        75 04 95 01 81 03
        05 09 19 01 29 08 15 00 25 01 75 01 95 08 81 02
        C0");

    /// <summary>Signed 16 bit axes (not usable by the firmware) + 4 buttons in byte 4.</summary>
    public static readonly byte[] SignedAxes = Hex(@"
        05 01 09 05 A1 01
        16 00 80 26 FF 7F 75 10 95 02 09 30 09 31 81 02
        05 09 19 01 29 04 15 00 25 01 75 01 95 04 81 02
        75 04 95 01 81 03
        C0");

    /// <summary>Cheap pad: D-pad reported as X/Y axes (0/127/255), 10 buttons, no hat.</summary>
    public static readonly byte[] AxisDpad = Hex(@"
        05 01 09 04 A1 01
        09 01 A1 00 09 30 09 31 15 00 26 FF 00 75 08 95 02 81 02 C0
        05 09 19 01 29 0A 15 00 25 01 75 01 95 0A 81 02
        75 06 95 01 81 03
        C0");

    /// <summary>D-pad as four usage bits (0x90-0x93) + 4 buttons, all in byte 0.</summary>
    public static readonly byte[] DpadUsages = Hex(@"
        05 01 09 05 A1 01
        15 00 25 01 75 01 95 04 09 90 09 91 09 92 09 93 81 02
        05 09 19 01 29 04 95 04 81 02
        C0");
}
