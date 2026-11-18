using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UsbToC64.App.Dialogs;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Core.Validation;
using UsbToC64.Core.XInput;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

public sealed record IssueItem(string Icon, Brush Brush, string Text);

public partial class GamepadPage : UserControl
{
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));

    private readonly MainWindow _w;
    private PadSession Session => _w.Pad;
    private AdapterHub Hub => _w.Hub;
    private readonly ObservableCollection<ControlRow> _rows = new();
    private PadDescription? _rowsFor;
    private bool _rowsViewOnly;
    private byte[]? _shownReport;
    private bool _fillingCombo;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    public GamepadPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        Table.ItemsSource = _rows;
        OpPanel.Attach(Hub);
        Session.Changed += () => Dispatcher.BeginInvoke(Rebuild);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => SetBusy(busy));
        Hub.StatusChanged += () => Dispatcher.BeginInvoke(() => Preview.SetMode(Hub.Status.Info?.IsAmiga));
        // Rows (labels of kinds and actions) are rebuilt in the new language; the mapping lives in the session.
        L.Changed += () => Dispatcher.BeginInvoke(() => { _rowsFor = null; _shownReport = null; Rebuild(); });
        _timer.Tick += (_, _) => RefreshLive();
        _timer.Start();
        Rebuild();
    }

    // ------------------------------------------------------------------ structure

    private void Rebuild()
    {
        FillPadCombo();
        bool hasPad = Session.Pad != null;

        if (Session.Pad != null && Session.Mapping != null)
        {
            if (!ReferenceEquals(_rowsFor, Session.Pad) || _rowsViewOnly != Session.ViewOnly)
            {
                _rows.Clear();
                foreach (var c in Session.Pad.Controls)
                    _rows.Add(new ControlRow(c, Session.Mapping.Get(c), !Session.ViewOnly, (ctl, a) => Session.SetAction(ctl, a)));
                _rowsFor = Session.Pad;
                _rowsViewOnly = Session.ViewOnly;
            }
            else
            {
                foreach (var r in _rows) r.Sync(Session.Mapping.Get(r.Control));
            }
        }
        else
        {
            _rows.Clear();
            _rowsFor = null;
        }

        var d = Session.Device;
        var pad = Session.Pad;
        KindChip.Visibility = CountChip.Visibility = pad != null ? Visibility.Visible : Visibility.Collapsed;
        if (d != null && pad != null)
        {
            KindText.Text = d.IsXInput
                ? d.XInputKind == XInputKind.Gip ? "XInput · Xbox One/Series" : "XInput · Xbox 360"
                : pad.UsesReportIds ? L.T("gp.kind.hidReportId") : "HID";
            CountText.Text = L.F("gp.count", pad.Controls.Count);
        }
        PadHint.Text = L.T(Session.ViewOnly ? "gp.hint.viewOnly" : Session.Offline ? "gp.hint.offline" : "gp.hint.press");
        if (Session.Offline && pad != null)
        {
            KindText.Text = L.T(Session.OfflineFrom switch
            {
                OfflineSource.Card => "gp.offline.card",
                OfflineSource.KnownXbox => "gp.offline.xbox",
                _ => "gp.offline.rules",
            });
            CountText.Text = L.F("gp.count", pad.Controls.Count);
        }
        ShowEditBar();

        EmptyText.Visibility = !hasPad && Session.Profile == null ? Visibility.Visible : Visibility.Collapsed;
        ShowView();

        var p = Session.Profile;
        RulesBox.Text = p == null ? "" : RulesText(p);
        JsonBox.Text = p == null ? "" : SaveJmJson.Serialize(p);
        FillIssues();
        ExportBtn.IsEnabled = p != null;
        WriteBtn.IsEnabled = p != null && !Hub.IsBusy;
    }

    private void ShowEditBar()
    {
        var slot = Session.EditSlot;
        EditBar.Visibility = slot != null ? Visibility.Visible : Visibility.Collapsed;
        if (slot == null) return;
        EditTitle.Text = L.F("gp.slot.title", slot.Number, slot.DisplayLabel, slot.Ids);
        EditText.Text = !Session.Offline ? L.T("gp.slot.live")
            : L.T(Session.OfflineFrom switch
            {
                OfflineSource.Card => "gp.slot.offlineCard",
                OfflineSource.KnownXbox => "gp.slot.offlineXbox",
                _ => "gp.slot.offlineRules",
            });
    }

    private void CloseSlot_Click(object sender, RoutedEventArgs e) => Session.CloseSlot();

    private void FillPadCombo()
    {
        _fillingCombo = true;
        PadCombo.Items.Clear();
        if (Session.Devices.Count == 0) PadCombo.Items.Add(L.T("gp.noPadItem"));
        foreach (var d in Session.Devices) PadCombo.Items.Add(d.ToString());
        PadCombo.SelectedIndex = Session.Device == null ? (Session.Offline ? -1 : 0) : Math.Max(0, Session.Devices.IndexOf(Session.Device));
        _fillingCombo = false;
    }

    private static string RulesText(JmProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VID {p.Vid:X4}  PID {p.Pid:X4}  {p.DeviceName}");
        sb.AppendLine(L.F("gp.rules.count", p.Rules.Count, FirmwareLimits.MaxRules));
        for (int i = 0; i < p.Rules.Count; i++) sb.AppendLine($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
        if (p.UseAnalog) sb.AppendLine(L.F("gp.rules.analog", string.Join(", ", p.AnalogX), string.Join(", ", p.AnalogY)));
        return sb.ToString();
    }

    private void FillIssues()
    {
        var items = new List<IssueItem>();
        var p = Session.Profile;
        if (p != null)
        {
            foreach (var i in Session.Issues.OrderByDescending(i => i.Severity))
                items.Add(i.Severity switch
                {
                    Severity.Error => new IssueItem("✖", ErrBrush, i.Message),
                    Severity.Warning => new IssueItem("⚠", WarnBrush, i.Message),
                    _ => new IssueItem("ⓘ", InfoBrush, i.Message),
                });
            foreach (var n in Session.ExtraNotes) items.Add(new IssueItem("⚠", WarnBrush, n));
            if (!Session.Issues.Any(i => i.Severity == Severity.Error))
                items.Insert(0, new IssueItem("✔", OkBrush, L.T("gp.issues.ok")));
            items.Add(new IssueItem("ⓘ", InfoBrush,
                L.F("gp.issues.rules", p.Rules.Count, FirmwareLimits.MaxRules)
                + (p.UseAnalog ? L.F("gp.issues.axes", string.Join(", ", p.AnalogX), string.Join(", ", p.AnalogY)) : "")));
        }
        if (Session.Pad != null) foreach (var n in Session.Pad.Notes) items.Add(new IssueItem("ⓘ", InfoBrush, n));
        if (Session.ViewHeader != null) foreach (var n in Session.ViewHeader.Notes) items.Add(new IssueItem("ⓘ", InfoBrush, n));
        IssuesList.ItemsSource = items;
    }

    private void ShowView()
    {
        bool any = Session.Pad != null || Session.Profile != null;
        Table.Visibility = ViewTable.IsChecked == true && Session.Pad != null ? Visibility.Visible : Visibility.Collapsed;
        RulesBox.Visibility = ViewRules.IsChecked == true && any ? Visibility.Visible : Visibility.Collapsed;
        JsonBox.Visibility = ViewJson.IsChecked == true && any ? Visibility.Visible : Visibility.Collapsed;
        // Read-only profile without a pad: show its rules instead of an empty table.
        if (Session.Pad == null && Session.Profile != null && ViewTable.IsChecked == true) RulesBox.Visibility = Visibility.Visible;
    }

    private void View_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ShowView();
    }

    // ------------------------------------------------------------------ live

    private void RefreshLive()
    {
        var snap = Session.Snapshot();
        Preview.Update(snap.State, snap.Live && Session.Device != null);
        if (snap.Error != null) LastResult.Text = L.F("gp.live.error", snap.Error);
        var report = snap.Report;
        if (report == null || ReferenceEquals(report, _shownReport)) return;
        _shownReport = report;
        foreach (var r in _rows)
        {
            var c = r.Control;
            r.IsActive = c.Supported && c.IsActive(report);
            r.State = c.Kind switch
            {
                ControlKind.Button or ControlKind.DpadButton => c.ReadValue(report) != 0 ? L.T("gp.state.pressed") : "",
                ControlKind.Hat => c.IsActive(report) ? HatLabel(c.ReadValue(report) - c.HatBase, c.HatPositions) : "",
                _ => c.Signed ? ((sbyte)c.ReadValue(report)).ToString() : c.ReadValue(report).ToString(),
            };
        }
    }

    private static string HatLabel(int pos, int positions)
    {
        var names = positions == 4
            ? new[] { L.T("gp.hat.n"), L.T("gp.hat.e"), L.T("gp.hat.s"), L.T("gp.hat.w") }
            : new[] { L.T("gp.hat.n"), L.T("gp.hat.ne"), L.T("gp.hat.e"), L.T("gp.hat.se"),
                      L.T("gp.hat.s"), L.T("gp.hat.sw"), L.T("gp.hat.w"), L.T("gp.hat.nw") };
        return pos >= 0 && pos < names.Length ? names[pos] : "";
    }

    // ------------------------------------------------------------------ actions

    private void PadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombo || PadCombo.SelectedIndex < 0 || Session.Devices.Count == 0) return;
        if (Session.Device != null && Session.Devices.IndexOf(Session.Device) == PadCombo.SelectedIndex && !Session.ViewOnly) return;
        if (Session.EditSlot != null &&
            MessageBox.Show(_w, L.T("gp.slot.leave"), L.T("gp.slot.leaveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            FillPadCombo();
            return;
        }
        var err = Session.Select(PadCombo.SelectedIndex);
        if (err != null) LastResult.Text = err;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var err = Session.RefreshDevices();
        LastResult.Text = err ?? (Session.Devices.Count == 0 ? L.T("gp.noPad") : "");
    }

    private void Propose_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Pad == null) { LastResult.Text = L.T("gp.propose.needPad"); return; }
        Session.Propose();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = L.T("gp.import.title"), Filter = L.T("gp.import.filter") };
        if (dlg.ShowDialog(_w) != true) return;
        PadSession.ImportOutcome o;
        try { o = Session.Import(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(_w, ex.Message, L.T("gp.import.failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (o.NeedsChoice)
        {
            var a = MessageBox.Show(_w, o.Question, L.T("gp.import.mismatch"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (a == MessageBoxResult.Cancel) return;
            if (a == MessageBoxResult.Yes) { o.OpenReadOnly(); LastResult.Text = L.T("gp.import.readOnly"); return; }
        }
        o.ApplyToPad();
        LastResult.Text = o.Summary;
    }

    /// <param name="question">Closing question, already translated ("Export anyway?").</param>
    private bool ConfirmIfErrors(string question)
    {
        var p = Session.Profile;
        if (p == null) return false;
        var errors = ProfileValidator.Validate(p, Session.ViewHeader).Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count == 0) return true;
        var msg = L.F("gp.errors.msg", errors.Count) + "\n\n" + string.Join("\n", errors.Select(e => "• " + e.Message)) + "\n\n" + question;
        return MessageBox.Show(_w, msg, L.T("gp.errors.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmIfErrors(L.T("gp.confirm.export"))) return;
        var p = Session.Profile!;
        var dlg = new SaveFileDialog { Title = L.T("gp.export.title"), Filter = "JSON (*.json)|*.json", FileName = $"profilo_{p.Vid:X4}_{p.Pid:X4}.json" };
        if (dlg.ShowDialog(_w) != true) return;
        File.WriteAllText(dlg.FileName, SaveJmJson.Serialize(p) + "\n");
        LastResult.Text = L.F("gp.exported", Path.GetFileName(dlg.FileName));
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy || !ConfirmIfErrors(L.T("gp.confirm.write"))) return;
        var p = Session.Profile!;
        var d = Session.Device;
        if (d != null && (p.Vid != d.Vid || p.Pid != d.Pid) &&
            MessageBox.Show(_w, L.F("gp.write.otherPad", p.Vid, p.Pid),
                L.T("gp.write.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        // Firmware 1.3.0: choose the drawer and the label.
        int? slot = null;
        string? label = null;
        var st = Hub.Status;
        if (st.HasDrawers)
        {
            var used = st.Slots!.Select(x => x.Info).ToList();
            var own = used.FirstOrDefault(x => x.Vid == p.Vid && x.Pid == p.Pid);
            int proposed = Session.EditSlot?.Slot ?? SlotLabels.Choose(used, p.Vid, p.Pid);
            string proposedLabel = Session.EditSlot?.Label is { Length: > 0 } el ? el
                : own is { IsWebConfigured: false } ? own.Label
                : SlotLabels.Auto(d?.Name, p.Vid, p.Pid);
            var dlg = SlotDialog.AskSave(_w, used, p.Vid, p.Pid, proposed, proposedLabel);
            if (dlg == null) return;
            slot = dlg.Slot;
            label = dlg.Label;
        }

        OpPanel.Begin(L.T("gp.op.write"));
        var r = await Hub.WriteAsync(p, slot, label);
        if (r.Verified && r.Slot != null && Hub.Status.Slots?.FirstOrDefault(x => x.Info.Slot == r.Slot) is { } saved)
            Session.SlotSaved(saved.Info);
        OpPanel.End(L.T(r.Verified ? "gp.op.writeOk" : "gp.op.writeFail"));
        LastResult.Text = r.Verified
            ? r.Slot != null ? L.F("gp.savedSlot", r.Slot + 1, r.Label, DateTime.Now.ToString("HH:mm")) : L.F("gp.saved", DateTime.Now.ToString("HH:mm"))
            : r.Message;
        var text = r.Message + (r.Differences.Count > 0 ? "\n\n" + string.Join("\n", r.Differences.Select(x => "• " + x)) : "");
        MessageBox.Show(_w, text, r.Verified ? L.T("gp.written.title") : L.T("gp.write.title"),
            MessageBoxButton.OK, r.Verified ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin(L.T("gp.op.read"));
        try
        {
            var (s, slotInfo) = await Hub.ReadAsync(Session.TargetVid, Session.TargetPid);
            var p = s.Profile;
            if (p == null)
            {
                OpPanel.End(L.T("gp.op.readNone"));
                LastResult.Text = Hub.Status.HasDrawers ? L.T("gp.read.noneForPad") : L.T("gp.read.none");
                return;
            }
            if (slotInfo != null) Hub.Log(L.F("gp.log.slot", slotInfo.Number, slotInfo.DisplayLabel));
            Hub.Log(L.F("gp.log.profile", p.Vid, p.Pid, p.Rules.Count));
            for (int i = 0; i < p.Rules.Count; i++) Hub.Log($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
            if (Session.Profile != null)
            {
                var diff = Core.Device.ProfileComparer.Compare(Session.Profile, p);
                foreach (var x in diff) Hub.Log("  ≠ " + x);
                LastResult.Text = diff.Count == 0 ? L.T("gp.read.same") : L.F("gp.read.different", diff.Count);
            }
            else LastResult.Text = L.F("gp.read.summary", p.Vid, p.Pid, p.Rules.Count);
            OpPanel.End(L.T("gp.op.readOk"));
        }
        catch (Exception ex)
        {
            Hub.Log(L.F("gp.log.error", ex.Message));
            OpPanel.End(L.T("gp.op.readFail"));
            LastResult.Text = ex.Message;
        }
    }

    private void SetBusy(bool busy)
    {
        WriteBtn.IsEnabled = !busy && Session.Profile != null;
        ReadBtn.IsEnabled = !busy;
        ImportBtn.IsEnabled = !busy;
    }
}
