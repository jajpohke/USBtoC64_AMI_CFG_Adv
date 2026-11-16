using System.Windows;
using System.Windows.Controls;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

public partial class SettingsPage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private AdapterInfo? _info;
    private bool _loadedOnce;

    public SettingsPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => LoadBtn.IsEnabled = ApplyBtn.IsEnabled = FactoryBtn.IsEnabled = !busy);
        IsVisibleChanged += async (_, e) => { if ((bool)e.NewValue && !_loadedOnce && Hub.Status.Info != null) await LoadAsync(); };
        Show(null, null);
    }

    private void Show(AdapterInfo? info, MemoryStatus? mem)
    {
        _info = info;
        ModeText.Text = info == null ? "—" : info.IsAmiga ? "Amiga" : "C64";
        Invert.IsChecked = info?.InvertSwitch == true;
        DevMode.IsChecked = info?.DevMode == true;
        DevAmiga.IsChecked = info?.DevAmiga == true;
        DevAmiga.IsEnabled = info?.DevMode == true;
        static string S(bool? b) => b == null ? "—" : b.Value ? "● salvato" : "○ predefinito";
        MemMouse.Text = S(mem?.Mouse); MemPad.Text = S(mem?.Pad); MemLed.Text = S(mem?.Led);
        ClearMouse.IsChecked = ClearPad.IsChecked = ClearLed.IsChecked = false;
        Invert.IsEnabled = DevMode.IsEnabled = ClearMouse.IsEnabled = ClearPad.IsEnabled = ClearLed.IsEnabled = info != null;
    }

    private async Task LoadAsync()
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin("Lettura delle impostazioni");
        try
        {
            var (info, mem) = await Hub.SessionAsync("Lettura impostazioni", s => (s.Ping(), s.GetMemoryStatus()));
            Show(info, mem);
            _loadedOnce = true;
            OpPanel.End("Impostazioni lette");
            Result.Text = $"Lette dall'adattatore alle {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex) { OpPanel.End("Lettura non riuscita"); Result.Text = ex.Message; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy || _info == null) { Result.Text = "Prima leggi le impostazioni dall'adattatore."; return; }
        bool inv = Invert.IsChecked == true != _info.InvertSwitch;
        bool dev = DevMode.IsChecked == true != _info.DevMode;
        bool ovr = _info.DevMode && (DevAmiga.IsChecked == true != _info.DevAmiga);
        var clear = new List<string>();
        if (ClearMouse.IsChecked == true) clear.Add("mouse");
        if (ClearPad.IsChecked == true) clear.Add("pad");
        if (ClearLed.IsChecked == true) clear.Add("led");
        if (!inv && !dev && !ovr && clear.Count == 0) { Result.Text = "Nessuna modifica da applicare."; return; }

        var msg = "Applicare le modifiche e riavviare l'adattatore?";
        if (dev && DevMode.IsChecked == true)
            msg += "\n\n⚠ Attivi la Developer Mode: dopo il riavvio la seriale passa ai pin 43/44 e da questo programma " +
                   "non sarà più possibile comunicare con l'adattatore.";
        if (clear.Count > 0) msg += $"\n\nVerranno cancellati: {string.Join(", ", clear)}.";
        if (MessageBox.Show(_w, msg, "Applica e riavvia", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        OpPanel.Begin("Applicazione delle impostazioni");
        try
        {
            await Hub.SessionAsync("Applicazione impostazioni", s =>
            {
                if (inv) s.ToggleInvertSwitch();
                if (ovr) s.ToggleDevAmiga();
                if (dev) s.ToggleDevMode();
                foreach (var t in clear) s.ClearMemory(t);
                s.Reboot();
                return true;
            });
            OpPanel.End("Impostazioni applicate, riavvio in corso");
            Result.Text = "Impostazioni applicate. L'adattatore si sta riavviando…";
        }
        catch (Exception ex) { OpPanel.End("Applicazione non riuscita"); Result.Text = ex.Message; return; }
        await AfterRestart();
    }

    private async void Factory_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        if (MessageBox.Show(_w,
                "Factory reset: vengono cancellate TUTTE le impostazioni salvate sull'adattatore (profilo pad, mouse, LED, " +
                "Developer Mode, inversione dello switch). L'operazione non si può annullare.\n\nContinuare?",
                "Factory reset", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        OpPanel.Begin("Factory reset");
        try
        {
            await Hub.SessionAsync("Factory reset", s => { s.FactoryReset(); return true; });
            OpPanel.End("Factory reset eseguito, riavvio in corso");
            Result.Text = "Factory reset eseguito. L'adattatore si sta riavviando…";
        }
        catch (Exception ex) { OpPanel.End("Factory reset non riuscito"); Result.Text = ex.Message; return; }
        await AfterRestart();
    }

    private async Task AfterRestart()
    {
        await Task.Delay(5000);
        await _w.CheckAdapterAsync();
        if (Hub.Status.Info != null) await LoadAsync();
        else Result.Text = "L'adattatore non risponde dopo il riavvio (normale se hai attivato la Developer Mode).";
    }
}
