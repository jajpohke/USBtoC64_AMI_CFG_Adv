using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UsbToC64.App.Services;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
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
                : pad.UsesReportIds ? "HID · con report ID" : "HID";
            CountText.Text = $"{pad.Controls.Count} controlli riconosciuti";
        }
        PadHint.Text = Session.ViewOnly ? "Profilo importato in sola lettura: «Proposta automatica» torna all'editor"
                                        : "Premi un tasto del pad: la riga si evidenzia";

        EmptyText.Visibility = !hasPad && Session.Profile == null ? Visibility.Visible : Visibility.Collapsed;
        ShowView();

        var p = Session.Profile;
        RulesBox.Text = p == null ? "" : RulesText(p);
        JsonBox.Text = p == null ? "" : SaveJmJson.Serialize(p);
        FillIssues();
        ExportBtn.IsEnabled = p != null;
        WriteBtn.IsEnabled = p != null && !Hub.IsBusy;
    }

    private void FillPadCombo()
    {
        _fillingCombo = true;
        PadCombo.Items.Clear();
        if (Session.Devices.Count == 0) PadCombo.Items.Add("(nessun gamepad trovato)");
        foreach (var d in Session.Devices) PadCombo.Items.Add(d.ToString());
        PadCombo.SelectedIndex = Session.Device == null ? 0 : Math.Max(0, Session.Devices.IndexOf(Session.Device));
        _fillingCombo = false;
    }

    private static string RulesText(JmProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VID {p.Vid:X4}  PID {p.Pid:X4}  {p.DeviceName}");
        sb.AppendLine($"Regole: {p.Rules.Count}/{FirmwareLimits.MaxRules}");
        for (int i = 0; i < p.Rules.Count; i++) sb.AppendLine($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
        if (p.UseAnalog) sb.AppendLine($"Assi analogici: X [{string.Join(", ", p.AnalogX)}]   Y [{string.Join(", ", p.AnalogY)}]");
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
                items.Insert(0, new IssueItem("✔", OkBrush, "Nessun errore: il profilo si può scrivere."));
            items.Add(new IssueItem("ⓘ", InfoBrush,
                $"{p.Rules.Count} regole su {FirmwareLimits.MaxRules}" + (p.UseAnalog ? $" · assi X [{string.Join(", ", p.AnalogX)}] Y [{string.Join(", ", p.AnalogY)}]" : "")));
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
        if (snap.Error != null) LastResult.Text = $"Lettura del pad interrotta: {snap.Error}";
        var report = snap.Report;
        if (report == null || ReferenceEquals(report, _shownReport)) return;
        _shownReport = report;
        foreach (var r in _rows)
        {
            var c = r.Control;
            r.IsActive = c.Supported && c.IsActive(report);
            r.State = c.Kind switch
            {
                ControlKind.Button or ControlKind.DpadButton => c.ReadValue(report) != 0 ? "premuto" : "",
                ControlKind.Hat => c.IsActive(report) ? HatLabel(c.ReadValue(report) - c.HatBase, c.HatPositions) : "",
                _ => c.Signed ? ((sbyte)c.ReadValue(report)).ToString() : c.ReadValue(report).ToString(),
            };
        }
    }

    private static string HatLabel(int pos, int positions)
    {
        string[] eight = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        string[] four = { "N", "E", "S", "O" };
        var names = positions == 4 ? four : eight;
        return pos >= 0 && pos < names.Length ? names[pos] : "";
    }

    // ------------------------------------------------------------------ actions

    private void PadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombo || PadCombo.SelectedIndex < 0 || Session.Devices.Count == 0) return;
        if (Session.Device != null && Session.Devices.IndexOf(Session.Device) == PadCombo.SelectedIndex && !Session.ViewOnly) return;
        var err = Session.Select(PadCombo.SelectedIndex);
        if (err != null) LastResult.Text = err;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var err = Session.RefreshDevices();
        LastResult.Text = err ?? (Session.Devices.Count == 0 ? "Nessun gamepad trovato." : "");
    }

    private void Propose_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Pad == null) { LastResult.Text = "Collega e seleziona un pad per la proposta automatica."; return; }
        Session.Propose();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Importa un profilo", Filter = "Profili (*.h;*.json)|*.h;*.json|Tutti i file (*.*)|*.*" };
        if (dlg.ShowDialog(_w) != true) return;
        PadSession.ImportOutcome o;
        try { o = Session.Import(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(_w, ex.Message, "Importazione non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (o.NeedsChoice)
        {
            var a = MessageBox.Show(_w, o.Question, "Il profilo non corrisponde a questo pad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (a == MessageBoxResult.Cancel) return;
            if (a == MessageBoxResult.Yes) { o.OpenReadOnly(); LastResult.Text = "Profilo aperto in sola lettura."; return; }
        }
        o.ApplyToPad();
        LastResult.Text = o.Summary;
    }

    private bool ConfirmIfErrors(string action)
    {
        var p = Session.Profile;
        if (p == null) return false;
        var errors = ProfileValidator.Validate(p, Session.ViewHeader).Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count == 0) return true;
        var msg = $"Il profilo ha {errors.Count} errori:\n\n" + string.Join("\n", errors.Select(e => "• " + e.Message)) + $"\n\n{action} comunque?";
        return MessageBox.Show(_w, msg, "Profilo con errori", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmIfErrors("Esportare")) return;
        var p = Session.Profile!;
        var dlg = new SaveFileDialog { Title = "Esporta il profilo (comando save_jm)", Filter = "JSON (*.json)|*.json", FileName = $"profilo_{p.Vid:X4}_{p.Pid:X4}.json" };
        if (dlg.ShowDialog(_w) != true) return;
        File.WriteAllText(dlg.FileName, SaveJmJson.Serialize(p) + "\n");
        LastResult.Text = $"Esportato: {Path.GetFileName(dlg.FileName)}";
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy || !ConfirmIfErrors("Scrivere sull'adattatore")) return;
        var p = Session.Profile!;
        var d = Session.Device;
        if (d != null && (p.Vid != d.Vid || p.Pid != d.Pid) &&
            MessageBox.Show(_w, $"Il profilo è per VID {p.Vid:X4} PID {p.Pid:X4}, diverso dal pad collegato al PC. Scriverlo comunque?",
                "Scrivi sull'adattatore", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        OpPanel.Begin("Scrittura del profilo");
        var r = await Hub.WriteAsync(p);
        OpPanel.End(r.Verified ? "Scrittura riuscita e verificata" : "Scrittura non riuscita");
        LastResult.Text = r.Verified ? $"Salvato e verificato alle {DateTime.Now:HH:mm}" : r.Message;
        var text = r.Message + (r.Differences.Count > 0 ? "\n\n" + string.Join("\n", r.Differences.Select(x => "• " + x)) : "");
        MessageBox.Show(_w, text, r.Verified ? "Profilo scritto e verificato" : "Scrittura sull'adattatore",
            MessageBoxButton.OK, r.Verified ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin("Lettura del profilo");
        try
        {
            var s = await Hub.ReadAsync();
            var p = s.Profile;
            if (p == null)
            {
                OpPanel.End("Nessun profilo pad salvato sull'adattatore");
                LastResult.Text = "Sull'adattatore non c'è un profilo pad.";
                return;
            }
            Hub.Log($"Profilo sull'adattatore: VID {p.Vid:X4} PID {p.Pid:X4}, {p.Rules.Count} regole");
            for (int i = 0; i < p.Rules.Count; i++) Hub.Log($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
            if (Session.Profile != null)
            {
                var diff = Core.Device.ProfileComparer.Compare(Session.Profile, p);
                foreach (var x in diff) Hub.Log("  ≠ " + x);
                LastResult.Text = diff.Count == 0 ? "L'adattatore contiene lo stesso profilo mostrato qui."
                                                  : $"L'adattatore contiene un profilo diverso ({diff.Count} differenze, vedi dettagli).";
            }
            else LastResult.Text = $"Profilo sull'adattatore: {p.Vid:X4}:{p.Pid:X4}, {p.Rules.Count} regole.";
            OpPanel.End("Lettura completata");
        }
        catch (Exception ex)
        {
            Hub.Log("Errore: " + ex.Message);
            OpPanel.End("Lettura non riuscita");
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
