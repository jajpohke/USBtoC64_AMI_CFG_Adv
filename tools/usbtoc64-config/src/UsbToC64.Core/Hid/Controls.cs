namespace UsbToC64.Core.Hid;

public enum ControlKind
{
    /// <summary>Single bit button.</summary>
    Button,
    /// <summary>Single bit D-pad direction (usages 0x90-0x93, or XInput D-pad bits).</summary>
    DpadButton,
    /// <summary>Hat switch (4 or 8 positions) in the low nibble of a byte.</summary>
    Hat,
    /// <summary>Absolute axis read through one byte (0..255, centre ~128).</summary>
    Axis,
}

public enum DpadDirection { Up, Down, Left, Right }

/// <summary>
/// A physical control of the pad, located in the raw report as the ESP32 sees it.
/// </summary>
public sealed record Control
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ControlKind Kind { get; init; }

    /// <summary>Index of the byte used by the firmware rule, in the ESP32 raw report.</summary>
    public required int ByteIndex { get; init; }
    /// <summary>Button: bit mask. Hat: 0x0F. Axis: 0xFF.</summary>
    public byte Mask { get; init; }

    /// <summary>Hat: value of the "north" position (logical minimum, usually 0, sometimes 1).</summary>
    public int HatBase { get; init; }
    /// <summary>Hat: 4 or 8 positions.</summary>
    public int HatPositions { get; init; } = 8;

    /// <summary>Axis usage: 0x30 X, 0x31 Y, ... (0 when not an HID axis).</summary>
    public ushort AxisUsage { get; init; }

    public DpadDirection? Dpad { get; init; }

    /// <summary>False when the firmware cannot use this control (see <see cref="Note"/>).</summary>
    public bool Supported { get; init; } = true;
    public string? Note { get; init; }

    /// <summary>True when the control is pressed / moved past the firmware thresholds in this report.</summary>
    public bool IsActive(ReadOnlySpan<byte> report) => Kind switch
    {
        ControlKind.Button or ControlKind.DpadButton => ReadValue(report) != 0,
        ControlKind.Hat => ReadValue(report) is var v && v >= HatBase && v < HatBase + HatPositions,
        ControlKind.Axis => Supported && ReadValue(report) is var a
                            && (a < Model.FirmwareLimits.AnalogDeadLow || a > Model.FirmwareLimits.AnalogDeadHigh),
        _ => false,
    };

    /// <summary>Reads the current state of the control from an ESP32-view report, for UI highlighting.</summary>
    public int ReadValue(ReadOnlySpan<byte> report)
    {
        if (ByteIndex < 0 || ByteIndex >= report.Length) return 0;
        byte b = report[ByteIndex];
        return Kind switch
        {
            ControlKind.Button or ControlKind.DpadButton => (b & Mask) != 0 ? 1 : 0,
            ControlKind.Hat => b & 0x0F,
            _ => b,
        };
    }
}

public sealed class PadDescription
{
    public required IReadOnlyList<Control> Controls { get; init; }
    public required bool UsesReportIds { get; init; }
    /// <summary>True when the layout is the native XInput one (not read from a descriptor).</summary>
    public bool IsXInput { get; init; }
    public List<string> Notes { get; } = new();
}
