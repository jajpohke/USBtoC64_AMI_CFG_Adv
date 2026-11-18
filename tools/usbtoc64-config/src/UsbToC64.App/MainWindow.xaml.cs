using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.App.Pages;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using Wpf.Ui.Controls;

namespace UsbToC64.App;

public partial class MainWindow : FluentWindow
{
    public AdapterHub Hub { get; } = new();
    public PadSession Pad { get; } = new();
    public AppSettings Settings => AppSettings.Current;
    private bool _advanced;

    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        _pages["gamepad"] = new GamepadPage(this);
        _pages["slots"] = new SlotsPage(this);
        _pages["mouse"] = new MousePage(this);
        _pages["led"] = new LedPage(this);
        _pages["settings"] = new SettingsPage(this);
        _pages["firmware"] = new FirmwarePage(this);
        _pages["terminal"] = new TerminalPage(this);
        _pages["log"] = new LogPage(this);
        SimpleHost.Content = new SimplePage(this);
        ShowAdvanced(Settings.StartAdvanced);

        Hub.StatusChanged += () => Dispatcher.BeginInvoke(UpdateAdapterCard);
        L.Changed += () => Dispatcher.BeginInvoke(() => { UpdateAdapterCard(); UpdateFirmwareBar(Hub.Status); });
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => AdapterCheck.IsEnabled = !busy);
        Loaded += async (_, _) => await StartupAsync();
        Closing += (_, e) =>
        {
            // Closing while writing the firmware or a profile can leave the adapter without a working program.
            if (Hub.IsBusy && System.Windows.MessageBox.Show(this, L.T("main.close.busy"), L.T("main.close.title"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            Pad.Dispose(); Hub.Dispose();
        };
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
        // The simple mode works on the connected pad: leave a drawer opened for editing.
        if (!advanced && Pad.EditSlot != null) Pad.CloseSlot();
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
        AdapterTitle.Text = L.T("main.adapter.checking");
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
        AdapterTitle.Text = L.F("main.esp.identifying", s.Port);
        var (found, desc) = await Hub.ProbeChipAsync();
        UpdateAdapterCard();
        if (!found)
        {
            Hub.Log(L.F("main.esp.notIdentified", s.Port, desc));
            return;
        }
        AdapterTitle.Text = L.F("main.esp.onPort", s.Port);
        AdapterLine1.Text = L.T("main.esp.silentLong");
        AdapterLine2.Text = desc;
        var fw = (FirmwarePage)_pages["firmware"];
        string included = Hub.Manifest == null ? "" : "\n\n" + L.F("main.esp.offer.yes", Hub.Manifest.Version);
        var answer = System.Windows.MessageBox.Show(this,
            L.F("main.esp.offer.text", s.Port, desc) + included + "\n" + L.T("main.esp.offer.noCancel"),
            L.T("main.esp.offer.title"),
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
            AdapterTitle.Text = L.F("main.esp.onPort", s.Port);
            AdapterLine1.Text = L.T("main.esp.silentShort");
            AdapterLine2.Text = L.T("main.esp.goFirmware");
            return;
        }
        if (s.Error != null || s.Info == null)
        {
            AdapterDot.Fill = Brushes.Gray;
            AdapterTitle.Text = L.T("main.adapter.notConnected");
            AdapterLine1.Text = s.Error != null ? L.T("main.adapter.connectHint") : "";
            AdapterLine2.Text = "";
            return;
        }
        AdapterDot.Fill = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        AdapterTitle.Text = L.F("main.adapter.onPort", s.Port);
        string fw = s.Firmware == null ? "" : s.Firmware.IsOriginal ? L.T("main.fw.original") : L.F("main.fw.version", s.Firmware.Version);
        AdapterLine1.Text = L.F("main.adapter.fwMode", fw, s.Info.IsAmiga ? "Amiga" : "C64");
        AdapterLine2.Text = s.HasDrawers ? L.F("main.pad.slots", s.Slots!.Count, FirmwareLimits.MaxSlots)
            : !s.StoredPadKnown ? L.T("main.pad.updateToRead")
            : s.StoredPad == null ? L.T("main.pad.none")
            : L.F("main.pad.stored", $"{s.StoredPad.Vid:X4}:{s.StoredPad.Pid:X4}");
    }

    private void UpdateFirmwareBar(AdapterStatus s)
    {
        if (s.Firmware == null || Hub.Manifest == null) { FirmwareBar.IsOpen = false; return; }
        if (FirmwareCheck.Compare(s.Firmware, Hub.Manifest) != FirmwareStatus.UpdateAvailable) { FirmwareBar.IsOpen = false; return; }
        FirmwareBar.Title = L.T("main.fwbar.title");
        FirmwareBar.Message = L.F("main.fwbar.message", s.Firmware.Version, Hub.Manifest.Version);
        FirmwareBar.IsOpen = true;
    }

    public void RefreshFirmwareBar() => UpdateFirmwareBar(Hub.Status);
}
