using System.Windows;
using System.Windows.Controls;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using Wpf.Ui.Controls;

namespace UsbToC64.App.Dialogs;

/// <summary>Choose the drawer and the label of a save, or just a new label (rename).</summary>
public partial class SlotDialog : FluentWindow
{
    private readonly IReadOnlyList<SlotInfo> _used;
    private readonly ushort _vid, _pid;

    public int Slot { get; private set; }
    public string Label { get; private set; } = "";

    private SlotDialog(Window owner, string title, string heading, string info, string ok,
        IReadOnlyList<SlotInfo> used, ushort vid, ushort pid, int? slot, string label)
    {
        InitializeComponent();
        Owner = owner;
        Title = title; Bar.Title = title;
        Heading.Text = heading;
        Info.Text = info;
        Info.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
        OkBtn.Content = ok;
        _used = used; _vid = vid; _pid = pid;
        if (slot == null) SlotPanel.Visibility = Visibility.Collapsed;
        else
        {
            for (int i = 0; i < FirmwareLimits.MaxSlots; i++)
            {
                var u = used.FirstOrDefault(s => s.Slot == i);
                SlotCombo.Items.Add(u == null ? L.F("sd.slot.free", i + 1) : L.F("sd.slot.used", i + 1, u.DisplayLabel, u.Ids));
            }
            SlotCombo.SelectedIndex = slot.Value;
            Slot = slot.Value;
        }
        LabelBox.Text = label;
        UpdateCounter();
        Loaded += (_, _) => { LabelBox.Focus(); LabelBox.SelectAll(); };
    }

    /// <summary>Save: drawer (proposed: the pad's own, else free, else oldest) and label.</summary>
    public static SlotDialog? AskSave(Window owner, IReadOnlyList<SlotInfo> used, ushort vid, ushort pid, int slot, string label)
    {
        var d = new SlotDialog(owner, L.T("sd.save.title"), L.F("sd.save.heading", $"{vid:X4}:{pid:X4}"), L.T("sd.save.info"),
            L.T("sd.save.ok"), used, vid, pid, slot, label);
        return d.ShowDialog() == true ? d : null;
    }

    /// <summary>Rename a drawer.</summary>
    public static string? AskRename(Window owner, SlotInfo s)
    {
        var d = new SlotDialog(owner, L.T("sd.rename.title"), L.F("sd.rename.heading", s.Number, s.Ids), "",
            L.T("sd.rename.ok"), Array.Empty<SlotInfo>(), s.Vid, s.Pid, null, s.Label);
        return d.ShowDialog() == true ? d.Label : null;
    }

    private void SlotCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Slot = Math.Max(0, SlotCombo.SelectedIndex);
        var occupant = _used.FirstOrDefault(s => s.Slot == Slot);
        var own = _used.FirstOrDefault(s => s.Vid == _vid && s.Pid == _pid);
        string warn = "";
        if (occupant != null && !(occupant.Vid == _vid && occupant.Pid == _pid))
            warn = L.F("sd.warn.replace", occupant.Number, occupant.DisplayLabel);
        if (own != null && own.Slot != Slot)
            warn += (warn.Length > 0 ? "\n" : "") + L.F("sd.warn.move", own.Number);
        SlotWarning.Text = warn;
        SlotWarning.Visibility = warn.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LabelBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCounter();

    private void UpdateCounter()
    {
        int bytes = SlotLabels.ByteCount(LabelBox.Text.Trim());
        Counter.Text = L.F("sd.counter", bytes, FirmwareLimits.LabelMaxBytes);
        Counter.Foreground = bytes > FirmwareLimits.LabelMaxBytes
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Label = SlotLabels.Clean(LabelBox.Text);
        DialogResult = true;
    }
}
