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

    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        _pages["gamepad"] = new GamepadPage(this);
        _pages["mouse"] = new ComingSoonPage("Mouse", "Velocità del mouse per C64 e Amiga, rotella, temporizzazione PAL/NTSC.");
        _pages["led"] = new ComingSoonPage("LED", "Colori del LED per modalità, fuoco e autofire.");
        _pages["settings"] = new ComingSoonPage("Impostazioni",
            "Modalità C64/Amiga e inversione dello switch, Developer Mode, cancellazione della memoria, factory reset.");
        _pages["firmware"] = new FirmwarePage(this);
        _pages["terminal"] = new TerminalPage(this);
        _pages["log"] = new LogPage(this);

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
        await CheckAdapterAsync();
    }

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

    private async void AdapterCheck_Click(object sender, RoutedEventArgs e) => await CheckAdapterAsync();

    /// <summary>Connection test, firmware version, stored profile; offers the firmware update when needed.</summary>
    public async Task CheckAdapterAsync()
    {
        if (Hub.IsBusy) return;
        Hub.RefreshPorts();
        AdapterTitle.Text = "Adattatore: verifica…";
        var s = await Hub.CheckAsync();
        UpdateFirmwareBar(s);
    }

    private void UpdateAdapterCard()
    {
        var s = Hub.Status;
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
