using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.App.Pages;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using Wpf.Ui.Controls;

namespace UsbToC64.App;

public partial class MainWindow : FluentWindow
{
    public AdapterHub Hub { get; } = new();
    public PadSession Pad { get; } = new();
    public AppSettings Settings { get; } = AppSettings.Load();
    private bool _advanced;

    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        _pages["gamepad"] = new GamepadPage(this);
        _pages["mouse"] = new MousePage(this);
        _pages["led"] = new LedPage(this);
        _pages["settings"] = new SettingsPage(this);
        _pages["firmware"] = new FirmwarePage(this);
        _pages["terminal"] = new TerminalPage(this);
        _pages["log"] = new LogPage(this);
        SimpleHost.Content = new SimplePage(this);
        ShowAdvanced(Settings.StartAdvanced);

        Hub.StatusChanged += () => Dispatcher.BeginInvoke(UpdateAdapterCard);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => AdapterCheck.IsEnabled = !busy);
        Loaded += async (_, _) => await StartupAsync();
        Closing += (_, _) => { Pad.Dispose(); Hub.Dispose(); };
        Nav.SelectedIndex = 0;
    }

    private async Task StartupAsync()
    {
        Pad.RefreshDevices();
        Hub.RefreshPorts();
        await CheckAdapterAsync(probeIfSilent: true);
    }

    public UserControl Page(string tag) => _pages[tag];

    /// <summary>Switches between the simple screen and the advanced configuration.</summary>
    public void ShowAdvanced(bool advanced)
    {
        _advanced = advanced;
        AdvancedRoot.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        SimpleHost.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Simple_Click(object sender, RoutedEventArgs e) => ShowAdvanced(false);

    public void Navigate(string tag)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is ListBoxItem { Tag: string tag } && _pages.TryGetValue(tag, out var page))
            Host.Content = page;
    }

    private async void AdapterCheck_Click(object sender, RoutedEventArgs e) => await CheckAdapterAsync(probeIfSilent: true);

    /// <summary>Connection test, firmware version, stored profile; offers the firmware update when needed.</summary>
    public async Task CheckAdapterAsync(bool probeIfSilent = false)
    {
        if (Hub.IsBusy) return;
        Hub.RefreshPorts();
        AdapterTitle.Text = "Adattatore: verifica…";
        var s = await Hub.CheckAsync();
        UpdateFirmwareBar(s);
        if (probeIfSilent && s.NoAnswer && _advanced) await OfferInstallOnSilentAdapterAsync(s);
    }

    /// <summary>
    /// An ESP32 port that does not answer: blank, in download mode (BOOT held) or another firmware.
    /// esptool identifies the chip without writing anything; then the user can install a firmware.
    /// </summary>
    private async Task OfferInstallOnSilentAdapterAsync(AdapterStatus s)
    {
        AdapterTitle.Text = $"ESP32 su {s.Port}: identificazione…";
        var (found, desc) = await Hub.ProbeChipAsync();
        UpdateAdapterCard();
        if (!found)
        {
            Hub.Log($"Il dispositivo su {s.Port} non risponde e non è stato identificato: {desc}");
            return;
        }
        AdapterTitle.Text = $"ESP32 su {s.Port}";
        AdapterLine1.Text = "Non risponde ai comandi: vergine, in download o con un altro firmware";
        AdapterLine2.Text = desc;
        var fw = (FirmwarePage)_pages["firmware"];
        string included = Hub.Manifest == null ? "" : $"\n\nSì = installa il firmware incluso ({Hub.Manifest.Version})";
        var answer = System.Windows.MessageBox.Show(this,
            $"Su {s.Port} c'è un ESP32 pronto per essere scritto ({desc}), ma non risponde ai comandi del programma: " +
            "può essere vergine, in modalità download (BOOT premuto) o avere un firmware diverso." +
            "\n\nVuoi installare un firmware? Tutta la memoria dell'adattatore verrà cancellata." +
            included + "\nNo = scegli un file .bin\nAnnulla = non fare niente",
            "Adattatore pronto per la scrittura",
            Hub.Manifest == null ? System.Windows.MessageBoxButton.OKCancel : System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.Cancel);
        if (answer == System.Windows.MessageBoxResult.Cancel) return;
        Navigate("firmware");
        if (answer == System.Windows.MessageBoxResult.Yes) await fw.InstallIncludedAsync(confirm: false);
        else await fw.InstallFromFileAsync();
    }

    private void UpdateAdapterCard()
    {
        var s = Hub.Status;
        if (s.NoAnswer)
        {
            AdapterDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));
            AdapterTitle.Text = $"ESP32 su {s.Port}";
            AdapterLine1.Text = "Non risponde: vergine, in download o con un altro firmware";
            AdapterLine2.Text = "Vai a Firmware per installarlo";
            return;
        }
        if (s.Error != null || s.Info == null)
        {
            AdapterDot.Fill = Brushes.Gray;
            AdapterTitle.Text = "Adattatore non collegato";
            AdapterLine1.Text = s.Error != null ? "Collegalo al PC con la USB-C, senza C64/Amiga." : "";
            AdapterLine2.Text = "";
            return;
        }
        AdapterDot.Fill = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        AdapterTitle.Text = $"Adattatore su {s.Port}";
        string fw = s.Firmware == null ? "" : s.Firmware.IsOriginal ? "Firmware originale (1.2)" : $"Firmware {s.Firmware.Version}";
        AdapterLine1.Text = $"{fw} · modalità {(s.Info.IsAmiga ? "Amiga" : "C64")}";
        AdapterLine2.Text = !s.StoredPadKnown ? "Profilo pad: aggiorna il firmware per leggerlo"
            : s.StoredPad == null ? "Nessun profilo pad salvato"
            : $"Profilo pad: {s.StoredPad.Vid:X4}:{s.StoredPad.Pid:X4}";
    }

    private void UpdateFirmwareBar(AdapterStatus s)
    {
        if (s.Firmware == null || Hub.Manifest == null) { FirmwareBar.IsOpen = false; return; }
        if (FirmwareCheck.Compare(s.Firmware, Hub.Manifest) != FirmwareStatus.UpdateAvailable) { FirmwareBar.IsOpen = false; return; }
        FirmwareBar.Title = "Aggiornamento firmware disponibile";
        FirmwareBar.Message = $"Sull'adattatore c'è il {s.Firmware}. Con il programma è incluso il firmware {Hub.Manifest.Version}. " +
                              "Apri la sezione Firmware per aggiornarlo.";
        FirmwareBar.IsOpen = true;
    }

    public void RefreshFirmwareBar() => UpdateFirmwareBar(Hub.Status);
}
