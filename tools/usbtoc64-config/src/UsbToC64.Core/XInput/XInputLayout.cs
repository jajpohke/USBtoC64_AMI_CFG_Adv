using UsbToC64.Core.Hid;

namespace UsbToC64.Core.XInput;

/// <summary>Native protocol of an XInput pad (what the ESP32 receives).</summary>
public enum XInputKind
{
    /// <summary>Xbox 360 wired and compatibles: 20-byte report starting with 00 14.</summary>
    Xbox360,
    /// <summary>Xbox One / Series and compatibles (GIP): report starting with 20; needs firmware 1.2.2+.</summary>
    Gip,
}

/// <summary>
/// Native XInput reports as received by the ESP32 (Xbox 360 wired, and Xbox One/Series GIP).
/// Windows does not expose them: it shows a synthetic HID report instead, with different byte indexes.
/// </summary>
public static class XInputLayout
{
    /// <summary>First firmware version that powers on GIP pads and filters Xbox status messages.</summary>
    public const string GipMinFirmware = "1.2.2";

    public const int ReportLength = 20;

    /// <summary>XINPUT_GAMEPAD.wButtons bits (same order as bytes 2-3 of the native report).</summary>
    [Flags]
    public enum Buttons : ushort
    {
        DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
        Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080,
        LeftShoulder = 0x0100, RightShoulder = 0x0200, Guide = 0x0400,
        A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
    }

    /// <summary>Wired Xbox 360 controllers known to send the native 20-byte report without initialisation.</summary>
    private static readonly HashSet<(ushort, ushort)> Known360 = new()
    {
        (0x045E, 0x028E), // Microsoft Xbox 360 Controller (wired)
    };

    /// <summary>Xbox One / Series controllers: GIP protocol, they need an init packet the firmware does not send.</summary>
    private static readonly HashSet<(ushort, ushort)> KnownGip = new()
    {
        (0x045E, 0x02D1), (0x045E, 0x02DD), (0x045E, 0x02E3), (0x045E, 0x02EA),
        (0x045E, 0x0B00), (0x045E, 0x0B12), (0x045E, 0x0B0A),
        (0x045E, 0x02FF), // virtual device created by Windows for GIP pads (never seen by the adapter)
    };

    /// <summary>Xbox 360 wireless receiver: different report format, not supported.</summary>
    private static readonly HashSet<(ushort, ushort)> KnownWirelessReceiver = new()
    {
        (0x045E, 0x0719), (0x045E, 0x0291),
    };

    public static bool IsKnownXbox360Wired(ushort vid, ushort pid) => Known360.Contains((vid, pid));
    /// <summary>Known Xbox One/Series (GIP) controllers.</summary>
    public static bool IsKnownXboxOne(ushort vid, ushort pid) => KnownGip.Contains((vid, pid));
    public static bool IsKnownWirelessReceiver(ushort vid, ushort pid) => KnownWirelessReceiver.Contains((vid, pid));
    public static bool IsKnownXInput(ushort vid, ushort pid) =>
        IsKnownXbox360Wired(vid, pid) || IsKnownXboxOne(vid, pid) || IsKnownWirelessReceiver(vid, pid);

    /// <summary>On Windows, the synthetic HID device of an XInput pad has "IG_" in its device path.</summary>
    public static bool IsXInputDevicePath(string? devicePath) =>
        devicePath != null && devicePath.Contains("IG_", StringComparison.OrdinalIgnoreCase);

    public static PadDescription Describe(XInputKind kind) => kind == XInputKind.Gip ? DescribeGip() : Describe();

    /// <summary>
    /// GIP input report (command 0x20): byte 4 Menu/View/A/B/X/Y, byte 5 D-pad/LB/RB/stick clicks,
    /// bytes 6-9 triggers (10 bit, little endian), bytes 10-17 sticks (int16, signed).
    /// </summary>
    public static PadDescription DescribeGip()
    {
        var c = new List<Control>
        {
            Dpad("dpad_up", "D-pad Up", 5, 0x01, DpadDirection.Up),
            Dpad("dpad_down", "D-pad Down", 5, 0x02, DpadDirection.Down),
            Dpad("dpad_left", "D-pad Left", 5, 0x04, DpadDirection.Left),
            Dpad("dpad_right", "D-pad Right", 5, 0x08, DpadDirection.Right),
            // Button order = default assignment order (same as the 360 layout)
            Btn("btn_a", "A", 4, 0x10),
            Btn("btn_b", "B", 4, 0x20),
            Btn("btn_x", "X", 4, 0x40),
            Btn("btn_y", "Y", 4, 0x80),
            Btn("btn_lb", "LB", 5, 0x10),
            Btn("btn_rb", "RB", 5, 0x20),
            Btn("btn_back", "View", 4, 0x08),
            Btn("btn_start", "Menu", 4, 0x04),
            Btn("btn_ls", "Click stick sinistro", 5, 0x40),
            Btn("btn_rs", "Click stick destro", 5, 0x80),
            // Triggers 0..1023: bit 1 of the high byte set = pressed more than half way
            Btn("trg_lt", "LT (oltre metà corsa)", 7, 0x02),
            Btn("trg_rt", "RT (oltre metà corsa)", 9, 0x02),
            Stick("axis_lx", "Stick sinistro X", 11, 0x30),
            Stick("axis_ly", "Stick sinistro Y", 13, 0x31),
            Stick("axis_rx", "Stick destro X", 15, 0x33),
            Stick("axis_ry", "Stick destro Y", 17, 0x34),
        };
        var d = new PadDescription { Controls = c, UsesReportIds = false, IsXInput = true };
        d.Notes.Add($"Pad Xbox One/Series (protocollo GIP): layout nativo del report che riceve l'ESP32. " +
                    $"Serve il firmware {GipMinFirmware} o successivo, che accende il controller e filtra i messaggi di stato.");
        return d;
    }

    /// <summary>GIP input report rebuilt from an XINPUT_GAMEPAD state, for the live preview.</summary>
    public static byte[] BuildGipReport(ushort buttons, byte leftTrigger, byte rightTrigger,
        short thumbLX, short thumbLY, short thumbRX, short thumbRY)
    {
        var r = new byte[18];
        r[0] = 0x20; r[1] = 0x00; r[2] = 0x00; r[3] = 0x0E;
        var b = (Buttons)buttons;
        byte b4 = 0, b5 = 0;
        if (b.HasFlag(Buttons.Start)) b4 |= 0x04;
        if (b.HasFlag(Buttons.Back)) b4 |= 0x08;
        if (b.HasFlag(Buttons.A)) b4 |= 0x10;
        if (b.HasFlag(Buttons.B)) b4 |= 0x20;
        if (b.HasFlag(Buttons.X)) b4 |= 0x40;
        if (b.HasFlag(Buttons.Y)) b4 |= 0x80;
        if (b.HasFlag(Buttons.DpadUp)) b5 |= 0x01;
        if (b.HasFlag(Buttons.DpadDown)) b5 |= 0x02;
        if (b.HasFlag(Buttons.DpadLeft)) b5 |= 0x04;
        if (b.HasFlag(Buttons.DpadRight)) b5 |= 0x08;
        if (b.HasFlag(Buttons.LeftShoulder)) b5 |= 0x10;
        if (b.HasFlag(Buttons.RightShoulder)) b5 |= 0x20;
        if (b.HasFlag(Buttons.LeftThumb)) b5 |= 0x40;
        if (b.HasFlag(Buttons.RightThumb)) b5 |= 0x80;
        r[4] = b4; r[5] = b5;
        void Put(int at, int v) { r[at] = (byte)(v & 0xFF); r[at + 1] = (byte)((v >> 8) & 0xFF); }
        Put(6, leftTrigger * 1023 / 255); Put(8, rightTrigger * 1023 / 255);
        Put(10, thumbLX); Put(12, thumbLY); Put(14, thumbRX); Put(16, thumbRY);
        return r;
    }

    public static PadDescription Describe()
    {
        var c = new List<Control>
        {
            Dpad("dpad_up", "D-pad Up", 2, 0x01, DpadDirection.Up),
            Dpad("dpad_down", "D-pad Down", 2, 0x02, DpadDirection.Down),
            Dpad("dpad_left", "D-pad Left", 2, 0x04, DpadDirection.Left),
            Dpad("dpad_right", "D-pad Right", 2, 0x08, DpadDirection.Right),
            // Button order = default assignment order
            Btn("btn_a", "A", 3, 0x10),
            Btn("btn_b", "B", 3, 0x20),
            Btn("btn_x", "X", 3, 0x40),
            Btn("btn_y", "Y", 3, 0x80),
            Btn("btn_lb", "LB", 3, 0x01),
            Btn("btn_rb", "RB", 3, 0x02),
            Btn("btn_back", "Back", 2, 0x20),
            Btn("btn_start", "Start", 2, 0x10),
            Btn("btn_ls", "Click stick sinistro", 2, 0x40),
            Btn("btn_rs", "Click stick destro", 2, 0x80),
            Btn("btn_guide", "Guide", 3, 0x04),
            // Triggers (0..255): bit 7 set = pressed more than half way
            Btn("trg_lt", "LT (oltre metà corsa)", 4, 0x80),
            Btn("trg_rt", "RT (oltre metà corsa)", 5, 0x80),
            Stick("axis_lx", "Stick sinistro X", 7, 0x30),
            Stick("axis_ly", "Stick sinistro Y", 9, 0x31),
            Stick("axis_rx", "Stick destro X", 11, 0x33),
            Stick("axis_ry", "Stick destro Y", 13, 0x34),
        };
        var d = new PadDescription { Controls = c, UsesReportIds = false, IsXInput = true };
        d.Notes.Add("Pad XInput: layout nativo del report a 20 byte (quello che riceve l'ESP32), non il report HID mostrato da Windows.");
        return d;
    }

    /// <summary>Builds the native report from an XINPUT_GAMEPAD state, for the live preview.</summary>
    public static byte[] BuildNativeReport(ushort buttons, byte leftTrigger, byte rightTrigger,
        short thumbLX, short thumbLY, short thumbRX, short thumbRY)
    {
        var r = new byte[ReportLength];
        r[0] = 0x00; r[1] = ReportLength;
        r[2] = (byte)(buttons & 0xFF); r[3] = (byte)(buttons >> 8);
        r[4] = leftTrigger; r[5] = rightTrigger;
        void Put(int at, short v) { r[at] = (byte)(v & 0xFF); r[at + 1] = (byte)((v >> 8) & 0xFF); }
        Put(6, thumbLX); Put(8, thumbLY); Put(10, thumbRX); Put(12, thumbRY);
        return r;
    }

    private static Control Dpad(string id, string name, int b, byte mask, DpadDirection d) => new()
    { Id = id, Name = name, Kind = ControlKind.DpadButton, ByteIndex = b, Mask = mask, Dpad = d };

    private static Control Btn(string id, string name, int b, byte mask) => new()
    { Id = id, Name = name, Kind = ControlKind.Button, ByteIndex = b, Mask = mask };

    private static Control Stick(string id, string name, int highByte, ushort usage) => new()
    {
        Id = id, Name = name, Kind = ControlKind.Axis, ByteIndex = highByte, Mask = 0xFF, AxisUsage = usage,
        Supported = false,
        Note = "Stick XInput signed (int16): le soglie del firmware (64/190, unsigned) non funzionano.",
    };
}
