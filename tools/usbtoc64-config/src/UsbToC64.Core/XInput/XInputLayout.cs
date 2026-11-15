using UsbToC64.Core.Hid;

namespace UsbToC64.Core.XInput;

/// <summary>
/// Native XInput report (Xbox 360 wired, 20 bytes) as received by the ESP32.
/// Windows does not expose it: it shows a synthetic HID report instead, with different byte indexes.
/// </summary>
public static class XInputLayout
{
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
        (0x045E, 0x0B00), (0x045E, 0x0B12),
    };

    /// <summary>Xbox 360 wireless receiver: different report format, not supported.</summary>
    private static readonly HashSet<(ushort, ushort)> KnownWirelessReceiver = new()
    {
        (0x045E, 0x0719), (0x045E, 0x0291),
    };

    public static bool IsKnownXbox360Wired(ushort vid, ushort pid) => Known360.Contains((vid, pid));
    public static bool IsKnownXboxOne(ushort vid, ushort pid) => KnownGip.Contains((vid, pid));
    public static bool IsKnownWirelessReceiver(ushort vid, ushort pid) => KnownWirelessReceiver.Contains((vid, pid));
    public static bool IsKnownXInput(ushort vid, ushort pid) =>
        IsKnownXbox360Wired(vid, pid) || IsKnownXboxOne(vid, pid) || IsKnownWirelessReceiver(vid, pid);

    /// <summary>On Windows, the synthetic HID device of an XInput pad has "IG_" in its device path.</summary>
    public static bool IsXInputDevicePath(string? devicePath) =>
        devicePath != null && devicePath.Contains("IG_", StringComparison.OrdinalIgnoreCase);

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
