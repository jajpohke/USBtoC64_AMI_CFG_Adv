using UsbToC64.Core.Model;

namespace UsbToC64.Core.Emulation;

/// <summary>What the C64/Amiga would see on the DB9 port.</summary>
public readonly record struct C64State(
    bool Up, bool Down, bool Left, bool Right,
    bool Fire1, bool Fire2, bool Fire3,
    bool Autofire)
{
    public override string ToString() =>
        $"{(Up ? "U" : ".")}{(Down ? "D" : ".")}{(Left ? "L" : ".")}{(Right ? "R" : ".")} " +
        $"F1:{(Fire1 ? "X" : ".")} F2:{(Fire2 ? "X" : ".")} F3:{(Fire3 ? "X" : ".")} AUTO:{(Autofire ? "ON" : "--")}";
}

/// <summary>
/// Replica of the firmware dynamic engine (InputEngine.h, branch <c>use_html_configurator</c>),
/// the one that runs profiles saved with <c>save_jm</c>. Keep in sync with the firmware.
/// </summary>
public sealed class DynamicEngine
{
    private readonly JmProfile _profile;
    private bool _autofireLatch; // html_autofire_latch (static in the firmware)

    public DynamicEngine(JmProfile profile) => _profile = profile;

    public C64State? Last { get; private set; }

    /// <summary>
    /// Evaluates one raw report (ESP32 view). Returns null when the firmware would ignore the report
    /// (shorter than 3 bytes): the outputs then keep their previous state.
    /// </summary>
    public C64State? Process(ReadOnlySpan<byte> report)
    {
        if (report.Length < FirmwareLimits.MinReportLength) return null;

        bool u = false, d = false, l = false, r = false;
        bool f1 = false, f2 = false, f3 = false;
        bool on = false, off = false, hold = false;

        foreach (var rule in _profile.Rules)
        {
            if (rule.Index >= report.Length) continue;
            byte v = report[rule.Index];
            bool matched = rule.Op switch
            {
                JmOp.BitAny => (v & rule.Value) != 0,
                JmOp.Hat => (v & 0x0F) == rule.Value,
                JmOp.SignedLess => (sbyte)v < (sbyte)rule.Value,
                JmOp.SignedGreater => (sbyte)v > (sbyte)rule.Value,
                _ => v == rule.Value,
            };
            if (!matched) continue;

            var f = rule.Func;
            if (f is JmFunc.Up or JmFunc.UpRight or JmFunc.LeftUp) u = true;
            if (f is JmFunc.Down or JmFunc.RightDown or JmFunc.DownLeft) d = true;
            if (f is JmFunc.Left or JmFunc.DownLeft or JmFunc.LeftUp) l = true;
            if (f is JmFunc.Right or JmFunc.UpRight or JmFunc.RightDown) r = true;
            if (f == JmFunc.Fire1) f1 = true;
            if (f == JmFunc.Fire2) f2 = true;
            if (f == JmFunc.Fire3) f3 = true;
            if (f == JmFunc.AutofireOn) on = true;
            if (f == JmFunc.AutofireOff) off = true;
            if (f == JmFunc.AutofireHold) hold = true;
            // AutoLeftRightOn/Off are not handled by the dynamic engine.
        }

        if (on) _autofireLatch = true;
        if (off) _autofireLatch = false;
        bool auto = _autofireLatch || hold;

        if (_profile.UseAnalog)
        {
            foreach (var idx in _profile.AnalogX.Take(FirmwareLimits.MaxAnalogPerAxis))
            {
                if (idx >= report.Length) continue;
                byte v = report[idx];
                if (v < FirmwareLimits.AnalogDeadLow) l = true;
                if (v > FirmwareLimits.AnalogDeadHigh) r = true;
            }
            foreach (var idx in _profile.AnalogY.Take(FirmwareLimits.MaxAnalogPerAxis))
            {
                if (idx >= report.Length) continue;
                byte v = report[idx];
                if (v < FirmwareLimits.AnalogDeadLow) u = true;
                if (v > FirmwareLimits.AnalogDeadHigh) d = true;
            }
        }

        var s = new C64State(u, d, l, r, f1, f2, f3, auto);
        Last = s;
        return s;
    }
}
