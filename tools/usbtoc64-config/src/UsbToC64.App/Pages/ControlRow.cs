using System.ComponentModel;
using System.Runtime.CompilerServices;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Text;
using PadControl = UsbToC64.Core.Hid.Control;

namespace UsbToC64.App.Pages;

public sealed record ActionOption(PadAction Action, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One row of the controls table.</summary>
public sealed class ControlRow : INotifyPropertyChanged
{
    private readonly Action<PadControl, PadAction> _onChange;
    private ActionOption? _selected;
    private string _state = "";
    private bool _active;
    private bool _suppress;

    public ControlRow(PadControl c, PadAction current, bool editable, Action<PadControl, PadAction> onChange)
    {
        Control = c;
        _onChange = onChange;
        Actions = c.Supported
            ? PadActions.AllowedFor(c.Kind).Select(a => new ActionOption(a, PadActions.Label(a))).ToList()
            : new List<ActionOption> { new(PadAction.None, L.T("row.unsupported")) };
        _selected = Actions.FirstOrDefault(a => a.Action == current) ?? Actions[0];
        CanEdit = editable && c.Supported;
    }

    public PadControl Control { get; }
    public string Name => Control.Name;
    public string Kind => Control.Kind switch
    {
        ControlKind.Button => L.T("row.kind.button"),
        ControlKind.DpadButton => "D-pad",
        ControlKind.Hat => L.T("row.kind.hat"),
        ControlKind.Axis => L.T("row.kind.axis"),
        _ => "",
    };
    public string Glyph => Control.Kind switch
    {
        ControlKind.Hat => "✛",
        ControlKind.DpadButton => "✛",
        ControlKind.Axis => Control.AxisUsage is 0x31 or 0x34 ? "↕" : "↔",
        _ => "●",
    };
    public int Byte => Control.ByteIndex;
    public string Bits => Control.Kind switch
    {
        ControlKind.Hat => $"{Control.HatBase}–{Control.HatBase + Control.HatPositions - 1}",
        ControlKind.Axis => Control.Signed ? L.T("row.signed") : "0–255",
        _ => $"0x{Control.Mask:X2}",
    };
    public string Note => Control.Note ?? "";
    public bool CanEdit { get; }
    public double RowOpacity => Control.Supported ? 1.0 : 0.55;
    public List<ActionOption> Actions { get; }

    public ActionOption? Selected
    {
        get => _selected;
        set
        {
            if (value == null || ReferenceEquals(value, _selected)) return;
            _selected = value;
            OnChanged();
            if (!_suppress) _onChange(Control, value.Action);
        }
    }

    /// <summary>Updates the selection from the model without calling back.</summary>
    public void Sync(PadAction a)
    {
        var opt = Actions.FirstOrDefault(o => o.Action == a);
        if (opt == null || ReferenceEquals(opt, _selected)) return;
        _suppress = true;
        Selected = opt;
        _suppress = false;
    }

    public string State { get => _state; set { if (_state != value) { _state = value; OnChanged(); } } }
    public bool IsActive { get => _active; set { if (_active != value) { _active = value; OnChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
