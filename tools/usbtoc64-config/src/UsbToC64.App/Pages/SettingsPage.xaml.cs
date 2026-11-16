using System.Windows;
using System.Windows.Controls;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Text;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

public partial class SettingsPage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private AdapterInfo? _info;
    private MemoryStatus? _mem;
    private bool _loadedOnce;

    public SettingsPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => LoadBtn.IsEnabled = ApplyBtn.IsEnabled = FactoryBtn.IsEnabled = !busy);
        IsVisibleChanged += async (_, e) => { if ((bool)e.NewValue && !_loadedOnce && Hub.Status.Info != null) await LoadAsync(); };
        Show(null, null);
        L.Changed += () => Dispatcher.BeginInvoke(ShowMemory);
    }

    private void Show(AdapterInfo? info, MemoryStatus? mem)
    {
        _info = info;
        ModeText.Text = info == null ? "—" : info.IsAmiga ? "Amiga" : "C64";
        Invert.IsChecked = info?.InvertSwitch == true;
        DevMode.IsChecked = info?.DevMode == true;
        DevAmiga.IsChecked = info?.DevAmiga == true;
        DevAmiga.IsEnabled = info?.DevMode == true;
        _mem = mem;
        ShowMemory();
        ClearMouse.IsChecked = ClearPad.IsChecked = ClearLed.IsChecked = false;
        Invert.IsEnabled = DevMode.IsEnabled = ClearMouse.IsEnabled = ClearPad.IsEnabled = ClearLed.IsEnabled = info != null;
    }

    private void ShowMemory()
    {
        static string S(bool? b) => b == null ? "—" : L.T(b.Value ? "set.mem.saved" : "set.mem.default");
        MemMouse.Text = S(_mem?.Mouse); MemPad.Text = S(_mem?.Pad); MemLed.Text = S(_mem?.Led);
    }

    private async Task LoadAsync()
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin(L.T("set.op.read"));
        try
        {
            var (info, mem) = await Hub.SessionAsync(L.T("set.session.read"), s => (s.Ping(), s.GetMemoryStatus()));
            Show(info, mem);
            _loadedOnce = true;
            OpPanel.End(L.T("set.read.done"));
            Result.Text = L.F("set.readAt", DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex) { OpPanel.End(L.T("set.read.failed")); Result.Text = ex.Message; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy || _info == null) { Result.Text = L.T("set.readFirst"); return; }
        bool inv = Invert.IsChecked == true != _info.InvertSwitch;
        bool dev = DevMode.IsChecked == true != _info.DevMode;
        bool ovr = _info.DevMode && (DevAmiga.IsChecked == true != _info.DevAmiga);
        var clear = new List<string>();
        var clearNames = new List<string>();
        if (ClearMouse.IsChecked == true) { clear.Add("mouse"); clearNames.Add(L.T("set.mem.mouse")); }
        if (ClearPad.IsChecked == true) { clear.Add("pad"); clearNames.Add(L.T("set.mem.pad")); }
        if (ClearLed.IsChecked == true) { clear.Add("led"); clearNames.Add(L.T("set.mem.led")); }
        if (!inv && !dev && !ovr && clear.Count == 0) { Result.Text = L.T("set.noChanges"); return; }

        var msg = L.T("set.apply.confirm");
        if (dev && DevMode.IsChecked == true)
            msg += "\n\n" + L.T("set.apply.devWarn");
        if (clear.Count > 0) msg += "\n\n" + L.F("set.apply.clear", string.Join(", ", clearNames));
        if (MessageBox.Show(_w, msg, L.T("set.apply.title"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        OpPanel.Begin(L.T("set.op.apply"));
        try
        {
            await Hub.SessionAsync(L.T("set.session.apply"), s =>
            {
                if (inv) s.ToggleInvertSwitch();
                if (ovr) s.ToggleDevAmiga();
                if (dev) s.ToggleDevMode();
                foreach (var t in clear) s.ClearMemory(t);
                s.Reboot();
                return true;
            });
            OpPanel.End(L.T("set.apply.done"));
            Result.Text = L.T("set.apply.result");
        }
        catch (Exception ex) { OpPanel.End(L.T("set.apply.failed")); Result.Text = ex.Message; return; }
        await AfterRestart();
    }

    private async void Factory_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        if (MessageBox.Show(_w,
                L.T("set.factory.confirm"),
                L.T("set.factory.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        OpPanel.Begin(L.T("set.factory.title"));
        try
        {
            await Hub.SessionAsync(L.T("set.factory.title"), s => { s.FactoryReset(); return true; });
            OpPanel.End(L.T("set.factory.done"));
            Result.Text = L.T("set.factory.result");
        }
        catch (Exception ex) { OpPanel.End(L.T("set.factory.failed")); Result.Text = ex.Message; return; }
        await AfterRestart();
    }

    private async Task AfterRestart()
    {
        await Task.Delay(5000);
        await _w.CheckAdapterAsync();
        if (Hub.Status.Info != null) await LoadAsync();
        else Result.Text = L.T("set.noAnswerAfter");
    }
}
