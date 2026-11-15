namespace UsbToC64.Core.Model;

/// <summary>Rule operator. Numeric values match the firmware enum <c>JM_Op</c>.</summary>
public enum JmOp : byte
{
    /// <summary><c>byte == value</c></summary>
    Eq = 0,
    /// <summary><c>(byte &amp; value) != 0</c></summary>
    BitAny = 1,
    /// <summary><c>(byte &amp; 0x0F) == value</c></summary>
    Hat = 2,
}

/// <summary>Rule function. Numeric values match the firmware enum <c>JM_Func</c> (current firmware).</summary>
public enum JmFunc : byte
{
    Up = 0,
    UpRight = 1,
    Right = 2,
    RightDown = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    LeftUp = 7,
    Fire1 = 8,
    Fire2 = 9,
    Fire3 = 10,
    AutofireOn = 11,
    AutofireOff = 12,
    AutofireHold = 13,
    AutoLeftRightOn = 14,
    AutoLeftRightOff = 15,
}

/// <summary>One mapping rule, as stored in the firmware NVS blob (<c>jm_blob</c>).</summary>
public readonly record struct JmRule(byte Index, byte Value, JmOp Op, JmFunc Func);

/// <summary>A complete pad profile as sent with <c>save_jm</c>.</summary>
public sealed class JmProfile
{
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public string? DeviceName { get; set; }

    /// <summary>Stored by the firmware but not used by the dynamic engine (informative).</summary>
    public sbyte DpadIndex { get; set; } = -1;

    public List<JmRule> Rules { get; } = new();
    public List<byte> AnalogX { get; } = new();
    public List<byte> AnalogY { get; } = new();

    public bool UseAnalog => AnalogX.Count > 0 || AnalogY.Count > 0;
    public bool IsEmpty => Rules.Count == 0 && !UseAnalog;
}

/// <summary>Limits and constants taken from the firmware source.</summary>
public static class FirmwareLimits
{
    /// <summary><c>JM_DYN_MAX_RULES</c> (Globals.h)</summary>
    public const int MaxRules = 32;
    /// <summary><c>JM_DYN_MAX_ANALOG</c> (Globals.h)</summary>
    public const int MaxAnalogPerAxis = 4;
    /// <summary><c>StaticJsonDocument&lt;3072&gt;</c> in the <c>save_jm</c> handler.</summary>
    public const int JsonDocumentCapacity = 3072;
    /// <summary><c>JM_ANALOG_DEAD_LOW</c>: below this value an axis means left/up.</summary>
    public const byte AnalogDeadLow = 64;
    /// <summary><c>JM_ANALOG_DEAD_HIGH</c>: above this value an axis means right/down.</summary>
    public const byte AnalogDeadHigh = 190;
    /// <summary>The firmware ignores reports shorter than this.</summary>
    public const int MinReportLength = 3;
    /// <summary>USB vendor id of the ESP32-S3 native CDC port (Espressif).</summary>
    public const ushort EspressifVid = 0x303A;

    public static bool IsDirection(JmFunc f) => f <= JmFunc.LeftUp;
}
