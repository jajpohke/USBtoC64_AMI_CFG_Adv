using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Core.Validation;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

/// <summary>
/// Simple mode: adapter ready? pad? save. The mapping is the automatic proposal (or the one customised in
/// the advanced mode) and is never shown here.
/// </summary>
public partial class SimplePage : UserControl
{
    private static readonly Brush OkBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x3A, 0x2A));
    private static readonly Brush OkFg = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
    private static readonly Brush WarnBg = new SolidColorBrush(Color.FromRgb(0x40, 0x32, 0x16));
    private static readonly Brush WarnFg = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));
    private static readonly Brush ErrBg = new SolidColorBrush(Color.FromRgb(0x44, 0x22, 0x24));
    private static readonly Brush ErrFg = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush CurBg = new SolidColorBrush(Color.FromRgb(0x16, 0x30, 0x3D));
    private static readonly Brush CurFg = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
    private Brush IdleBg => TryFindResource("ControlFillColorSecondaryBrush") as Brush ?? Brushes.DimGray;
    private Brush IdleFg => TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

    private enum AdapterStep { Checking, Ready, NeedsUpdate, NotFound, Blank }

    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private PadSession Session => _w.Pad;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private AdapterStep _adapter = AdapterStep.Checking;
    private bool _fillingCombo;
    private bool _saving;
    private bool _statusSeen;

    public SimplePage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        StartAdvanced.IsChecked = _w.Settings.StartAdvanced;
        Hub.StatusChanged += () => Dispatcher.BeginInvoke(() => { _statusSeen = true; Refresh(); });
        Hub.BusyChanged += _ => Dispatcher.BeginInvoke(Refresh);
        Session.Changed += () => Dispatcher.BeginInvoke(Refresh);
        L.Changed += () => Dispatcher.BeginInvoke(Refresh);
        _timer.Tick += (_, _) =>
        {
            var snap = Session.Snapshot();
            Preview.Update(snap.State, snap.Live && Session.Device != null);
        };
        _timer.Start();
        Refresh();
    }

    // ------------------------------------------------------------------ state

    /// <summary>Firmware needed by the current profile: XInput pads need the interface fix (1.2.2), sticks the signed rules (1.2.3).</summary>
    private FirmwareVersion? RequiredFirmware()
    {
        var p = Session.Profile;
        if (p == null) return null;
        if (p.Rules.Any(r => FirmwareLimits.IsSigned(r.Op))) return FirmwareVersion.Parse(FirmwareLimits.SignedOpsMinFirmware);
        if (Session.Device?.IsXInput == true) return FirmwareVersion.Parse(Core.XInput.XInputLayout.GipMinFirmware);
        return null;
    }

    private void Refresh()
    {
        var s = Hub.Status;
        var fw = s.Firmware;
        var m = Hub.Manifest;
        bool checking = Hub.IsBusy && !_saving;

        // ---- step 1
        AdapterAction.Visibility = AdapterFile.Visibility = AdapterRetry.Visibility = Visibility.Collapsed;
        AdapterRing.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
        if ((checking || !_statusSeen) && s.Info == null && !s.NoAnswer)
        {
            _adapter = AdapterStep.Checking;
            SetNum(Num1, Num1Text, "1", CurBg, CurFg);
            AdapterState.Text = L.T("simple.adapter.checking");
            AdapterHint.Text = "";
        }
        else if (s.NoAnswer)
        {
            _adapter = AdapterStep.Blank;
            SetNum(Num1, Num1Text, "!", WarnBg, WarnFg);
            AdapterState.Text = L.F("simple.adapter.blank", s.Port);
            AdapterHint.Text = L.T("simple.adapter.blankHint");
            if (m != null) { AdapterAction.Content = L.F("simple.adapter.install", m.Version); AdapterAction.Visibility = Visibility.Visible; }
            AdapterFile.Visibility = Visibility.Visible;
            AdapterRetry.Visibility = Visibility.Visible;
        }
        else if (s.Info == null)
        {
            _adapter = AdapterStep.NotFound;
            SetNum(Num1, Num1Text, "✖", ErrBg, ErrFg);
            AdapterState.Text = L.T("simple.adapter.notFound");
            AdapterHint.Text = L.T("simple.adapter.notFoundHint");
            AdapterRetry.Visibility = checking ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            var need = RequiredFirmware();
            bool tooOld = need != null && fw != null && (fw.IsOriginal || fw.Version < need);
            var cmp = fw != null && m != null ? FirmwareCheck.Compare(fw, m) : (FirmwareStatus?)null;
            bool update = cmp == FirmwareStatus.UpdateAvailable;
            string fwText = fw == null ? "" : fw.IsOriginal ? L.T("simple.fw.original") : L.F("simple.fw.version", fw.Version);
            string mode = s.Info.IsAmiga ? "Amiga" : "C64";
            if (update || tooOld)
            {
                _adapter = AdapterStep.NeedsUpdate;
                SetNum(Num1, Num1Text, "!", WarnBg, WarnFg);
                AdapterState.Text = L.F("simple.adapter.needsUpdate", s.Port, fwText);
                AdapterHint.Text = m == null
                    ? L.F("simple.adapter.needsFile", need)
                    : L.F("simple.adapter.updateHint", m.Version);
                if (m != null) { AdapterAction.Content = L.T("simple.adapter.update"); AdapterAction.Visibility = Visibility.Visible; }
                else AdapterFile.Visibility = Visibility.Visible;
            }
            else
            {
                _adapter = AdapterStep.Ready;
                SetNum(Num1, Num1Text, "✔", OkBg, OkFg);
                AdapterState.Text = L.F(cmp == FirmwareStatus.UpToDate ? "simple.adapter.readyUpToDate" : "simple.adapter.ready", s.Port, fwText);
                var names = s.StoredNames();
                AdapterHint.Text = L.F("simple.adapter.readyHint", mode)
                                   + (cmp == FirmwareStatus.OtherBuild ? " " + L.F("simple.adapter.otherBuild", fw!.Build, m!.Build) : "")
                                   + "\n" + (names.Count == 0 ? L.T("simple.adapter.noPads")
                                       : s.HasDrawers ? L.F("simple.adapter.pads", string.Join(", ", names), names.Count, FirmwareLimits.MaxSlots)
                                       : L.F("simple.adapter.padsOne", names[0]));
            }
        }

        // ---- step 2
        FillPadCombo();
        string? padProblem = PadProblem();
        bool padOk = Session.Device != null && Session.Profile != null && padProblem == null;
        if (Session.Device == null)
        {
            SetNum(Num2, Num2Text, "2", CurBg, CurFg);
            PadState.Foreground = WarnFg;
            PadState.Text = L.T("simple.pad.none");
        }
        else if (padProblem != null)
        {
            SetNum(Num2, Num2Text, "!", WarnBg, WarnFg);
            PadState.Foreground = WarnFg;
            PadState.Text = padProblem;
        }
        else
        {
            SetNum(Num2, Num2Text, "✔", OkBg, OkFg);
            PadState.Foreground = OkFg;
            PadState.Text = ReadyText(s);
        }

        // ---- step 3
        bool canSave = _adapter == AdapterStep.Ready && padOk && !Hub.IsBusy;
        SaveBtn.IsEnabled = canSave;
        if (!_saving && DoneCard.Visibility != Visibility.Visible)
            SetNum(Num3, Num3Text, "3", canSave ? CurBg : IdleBg, canSave ? CurFg : IdleFg);
        SaveHint.Text = L.T(canSave || _saving ? "simple.save.hint"
            : _adapter != AdapterStep.Ready ? "simple.save.step1First" : "simple.save.step2First");
    }

    /// <summary>"Ready", with where the profile is or will be saved.</summary>
    private string ReadyText(AdapterStatus s)
    {
        var p = Session.Profile!;
        var stored = s.StoredFor(p.Vid, p.Pid);
        bool same = stored != null && ProfileComparer.Compare(p, stored.Value.Profile).Count == 0;
        if (!s.HasDrawers) return L.T(same ? "simple.pad.readySaved" : "simple.pad.ready");
        var (slot, label, replaced) = Target(s);
        if (stored?.Slot is { } own)
            return same ? L.F("simple.pad.readySavedSlot", own.Number, own.DisplayLabel)
                        : L.F("simple.pad.readyUpdateSlot", own.Number, own.DisplayLabel);
        return replaced != null
            ? L.F("simple.pad.readyReplace", label, slot + 1, replaced.DisplayLabel)
            : L.F("simple.pad.readyNewSlot", label, slot + 1);
    }

    /// <summary>
    /// Where the simple mode saves (firmware 1.3.0): the pad's own drawer, else the first free one, else the oldest;
    /// label: the one the user gave the pad, else the automatic one (from the pad name).
    /// </summary>
    private (int Slot, string Label, SlotInfo? Replaced) Target(AdapterStatus s)
    {
        var p = Session.Profile!;
        var used = s.Slots!.Select(d => d.Info).ToList();
        int slot = SlotLabels.Choose(used, p.Vid, p.Pid);
        var occupant = used.FirstOrDefault(u => u.Slot == slot);
        bool own = occupant != null && occupant.Vid == p.Vid && occupant.Pid == p.Pid;
        string label = own && !occupant!.IsWebConfigured ? occupant.Label : SlotLabels.Auto(Session.Device?.Name, p.Vid, p.Pid);
        return (slot, label, own ? null : occupant);
    }

    /// <summary>Why the selected pad cannot be saved, in plain words; null when it can.</summary>
    private string? PadProblem()
    {
        var p = Session.Profile;
        if (Session.Device == null) return null;
        if (p == null || Session.Pad == null) return L.T("simple.pad.unknown");
        var errors = Session.Issues.Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count > 0) return errors[0].Message;
        var need = RequiredFirmware();
        var fw = Hub.Status.Firmware;
        if (need != null && fw != null && (fw.IsOriginal || fw.Version < need))
            return L.F("simple.pad.needsFw", need);
        return null;
    }

    private void FillPadCombo()
    {
        _fillingCombo = true;
        PadCombo.Items.Clear();
        if (Session.Devices.Count == 0) PadCombo.Items.Add(L.T("simple.pad.noneItem"));
        foreach (var d in Session.Devices) PadCombo.Items.Add($"{d.Name}   {d.Vid:X4}:{d.Pid:X4}");
        PadCombo.SelectedIndex = Session.Device == null ? 0 : Math.Max(0, Session.Devices.IndexOf(Session.Device));
        _fillingCombo = false;
    }

    private static void SetNum(Border b, TextBlock t, string text, Brush bg, Brush fg)
    {
        b.Background = bg; t.Foreground = fg; t.Text = text;
    }

    // ------------------------------------------------------------------ actions

    private void PadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingCombo || PadCombo.SelectedIndex < 0 || Session.Devices.Count == 0) return;
        if (Session.Device != null && Session.Devices.IndexOf(Session.Device) == PadCombo.SelectedIndex) return;
        DoneCard.Visibility = Visibility.Collapsed;
        Session.Select(PadCombo.SelectedIndex);
    }

    private void RefreshPads_Click(object sender, RoutedEventArgs e)
    {
        DoneCard.Visibility = Visibility.Collapsed;
        Session.RefreshDevices();
        Refresh();
    }

    private async void AdapterRetry_Click(object sender, RoutedEventArgs e) => await _w.CheckAdapterAsync(probeIfSilent: false);

    private async void AdapterAction_Click(object sender, RoutedEventArgs e)
    {
        var m = Hub.Manifest;
        if (m == null || Hub.IsBusy) return;
        if (MessageBox.Show(_w,
                L.F("simple.update.confirm", m.Version) + (Hub.StoredPadsWarning() is { } w ? "\n\n" + w : ""),
                L.T("simple.adapter.update"), MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        AdapterState.Text = L.T("simple.update.running");
        var r = await Hub.FlashAsync();
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        if (!r.Success) MessageBox.Show(_w, r.Message, L.T("simple.update.title"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void AdapterFile_Click(object sender, RoutedEventArgs e)
    {
        await ((FirmwarePage)_w.Page("firmware")).InstallFromFileAsync();
        Refresh();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = Session.Profile;
        if (p == null || Hub.IsBusy) return;
        int? slot = null;
        string? label = null;
        if (Hub.Status.HasDrawers)
        {
            var t = Target(Hub.Status);
            if (t.Replaced != null &&
                MessageBox.Show(_w, L.F("simple.save.replaceConfirm", FirmwareLimits.MaxSlots, t.Replaced.Number, t.Replaced.DisplayLabel),
                    L.T("simple.save.replaceTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                return;
            slot = t.Slot;
            label = t.Label;
        }
        _saving = true;
        DoneCard.Visibility = Visibility.Collapsed;
        SaveError.Visibility = Visibility.Collapsed;
        Saving.Visibility = Visibility.Visible;
        SaveBtn.IsEnabled = false;
        WriteResult r;
        try { r = await Hub.WriteAsync(p, slot, label); }
        finally
        {
            _saving = false;
            Saving.Visibility = Visibility.Collapsed;
        }
        if (r.Verified)
        {
            DoneTitle.Text = r.Slot != null ? L.F("simple.done.titleSlot", r.Label, r.Slot + 1) : L.T("simple.done.title");
            DoneCard.Visibility = Visibility.Visible;
            SetNum(Num3, Num3Text, "✔", OkBg, OkFg);
        }
        else
        {
            SaveError.Text = r.Message + (r.Differences.Count > 0 ? "\n" + string.Join("\n", r.Differences.Select(d => "• " + d)) : "") +
                             "\n" + L.T("simple.save.seeLog");
            SaveError.Visibility = Visibility.Visible;
            SetNum(Num3, Num3Text, "✖", ErrBg, ErrFg);
        }
        Refresh();
    }

    private void Advanced_Click(object sender, RoutedEventArgs e) => _w.ShowAdvanced(true);

    private void StartAdvanced_Changed(object sender, RoutedEventArgs e)
    {
        _w.Settings.StartAdvanced = StartAdvanced.IsChecked == true;
        _w.Settings.Save();
    }
}
