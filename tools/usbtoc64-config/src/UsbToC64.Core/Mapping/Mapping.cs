using UsbToC64.Core.Hid;
using UsbToC64.Core.Model;

namespace UsbToC64.Core.Mapping;

/// <summary>What the user assigns to a control.</summary>
public enum PadAction
{
    None,
    // single functions (buttons, D-pad buttons)
    Up, UpRight, Right, RightDown, Down, DownLeft, Left, LeftUp,
    Fire1, Fire2, Fire3, AutofireOn, AutofireOff, AutofireHold,
    // hat
    Directions,
    // axes
    AxisX, AxisY,
}

public static class PadActions
{
    public static IReadOnlyList<PadAction> AllowedFor(ControlKind kind) => kind switch
    {
        ControlKind.Hat => new[] { PadAction.None, PadAction.Directions },
        ControlKind.Axis => new[] { PadAction.None, PadAction.AxisX, PadAction.AxisY },
        _ => new[]
        {
            PadAction.None, PadAction.Fire1, PadAction.Fire2, PadAction.Fire3,
            PadAction.AutofireOn, PadAction.AutofireOff, PadAction.AutofireHold,
            PadAction.Up, PadAction.Down, PadAction.Left, PadAction.Right,
            PadAction.UpRight, PadAction.RightDown, PadAction.DownLeft, PadAction.LeftUp,
        },
    };

    public static JmFunc? ToFunc(PadAction a) => a switch
    {
        PadAction.Up => JmFunc.Up, PadAction.UpRight => JmFunc.UpRight,
        PadAction.Right => JmFunc.Right, PadAction.RightDown => JmFunc.RightDown,
        PadAction.Down => JmFunc.Down, PadAction.DownLeft => JmFunc.DownLeft,
        PadAction.Left => JmFunc.Left, PadAction.LeftUp => JmFunc.LeftUp,
        PadAction.Fire1 => JmFunc.Fire1, PadAction.Fire2 => JmFunc.Fire2, PadAction.Fire3 => JmFunc.Fire3,
        PadAction.AutofireOn => JmFunc.AutofireOn, PadAction.AutofireOff => JmFunc.AutofireOff,
        PadAction.AutofireHold => JmFunc.AutofireHold,
        _ => null,
    };

    public static string Label(PadAction a) => a switch
    {
        PadAction.None => "(nessuna)",
        PadAction.Directions => "Direzioni (8 vie)",
        PadAction.AxisX => "Asse orizzontale (sinistra/destra)",
        PadAction.AxisY => "Asse verticale (su/giù)",
        _ => ToFunc(a) is { } f ? Names.Func(f) : a.ToString(),
    };
}

/// <summary>Assignment of actions to the controls of a pad.</summary>
public sealed class PadMapping
{
    public required PadDescription Pad { get; init; }
    public Dictionary<string, PadAction> Actions { get; } = new();

    public PadAction Get(Control c) => Actions.TryGetValue(c.Id, out var a) ? a : PadAction.None;

    public void Set(Control c, PadAction a)
    {
        if (!PadActions.AllowedFor(c.Kind).Contains(a))
            throw new ArgumentException($"Azione {a} non valida per {c.Name} ({c.Kind}).");
        Actions[c.Id] = a;
    }
}

public static class MappingProposer
{
    /// <summary>Same default order as the web configurator (BTN_DEFAULT_SEQUENCE).</summary>
    public static readonly PadAction[] ButtonSequence =
    {
        PadAction.Fire1, PadAction.Up, PadAction.Fire2, PadAction.AutofireHold,
        PadAction.Fire3, PadAction.AutofireOff, PadAction.AutofireOn,
    };

    /// <summary>
    /// Automatic proposal: directional controls (hat, D-pad usages, first X/Y axes) become directions,
    /// buttons follow <see cref="ButtonSequence"/>. Unsupported controls stay unassigned.
    /// </summary>
    public static PadMapping Propose(PadDescription pad)
    {
        var m = new PadMapping { Pad = pad };
        var usable = pad.Controls.Where(c => c.Supported).ToList();

        var hat = usable.FirstOrDefault(c => c.Kind == ControlKind.Hat);
        if (hat != null) m.Set(hat, PadAction.Directions);

        foreach (var d in usable.Where(c => c.Kind == ControlKind.DpadButton))
            m.Set(d, d.Dpad switch
            {
                DpadDirection.Up => PadAction.Up,
                DpadDirection.Down => PadAction.Down,
                DpadDirection.Left => PadAction.Left,
                _ => PadAction.Right,
            });

        var x = usable.FirstOrDefault(c => c.Kind == ControlKind.Axis && c.AxisUsage == 0x30);
        var y = usable.FirstOrDefault(c => c.Kind == ControlKind.Axis && c.AxisUsage == 0x31);
        if (x != null) m.Set(x, PadAction.AxisX);
        if (y != null) m.Set(y, PadAction.AxisY);

        int n = 0;
        foreach (var b in usable.Where(c => c.Kind == ControlKind.Button))
        {
            if (n >= ButtonSequence.Length) break;
            m.Set(b, ButtonSequence[n++]);
        }
        return m;
    }
}

public static class ProfileBuilder
{
    private static readonly JmFunc[] Hat8 =
    {
        JmFunc.Up, JmFunc.UpRight, JmFunc.Right, JmFunc.RightDown,
        JmFunc.Down, JmFunc.DownLeft, JmFunc.Left, JmFunc.LeftUp,
    };
    private static readonly JmFunc[] Hat4 = { JmFunc.Up, JmFunc.Right, JmFunc.Down, JmFunc.Left };

    public static JmProfile Build(PadMapping mapping, ushort vid, ushort pid, string? deviceName = null)
    {
        var p = new JmProfile { Vid = vid, Pid = pid, DeviceName = deviceName };
        foreach (var c in mapping.Pad.Controls)
        {
            var a = mapping.Get(c);
            if (a == PadAction.None || !c.Supported) continue;
            byte idx = checked((byte)c.ByteIndex);
            switch (c.Kind)
            {
                case ControlKind.Hat when a == PadAction.Directions:
                    var dirs = c.HatPositions == 4 ? Hat4 : Hat8;
                    for (int k = 0; k < dirs.Length; k++)
                        p.Rules.Add(new JmRule(idx, (byte)(c.HatBase + k), JmOp.Hat, dirs[k]));
                    if (p.DpadIndex < 0) p.DpadIndex = (sbyte)idx;
                    break;
                case ControlKind.Axis when a == PadAction.AxisX:
                    if (!p.AnalogX.Contains(idx)) p.AnalogX.Add(idx);
                    break;
                case ControlKind.Axis when a == PadAction.AxisY:
                    if (!p.AnalogY.Contains(idx)) p.AnalogY.Add(idx);
                    break;
                case ControlKind.Button or ControlKind.DpadButton:
                    if (PadActions.ToFunc(a) is { } f)
                    {
                        p.Rules.Add(new JmRule(idx, c.Mask, JmOp.BitAny, f));
                        if (c.Kind == ControlKind.DpadButton && FirmwareLimits.IsDirection(f) && p.DpadIndex < 0)
                            p.DpadIndex = (sbyte)idx;
                    }
                    break;
            }
        }
        return p;
    }
}
