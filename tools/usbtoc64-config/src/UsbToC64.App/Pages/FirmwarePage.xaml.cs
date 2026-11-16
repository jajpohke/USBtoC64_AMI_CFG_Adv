using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace UsbToC64.App.Pages;

public partial class FirmwarePage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;

    public FirmwarePage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        Hub.StatusChanged += () => Dispatcher.BeginInvoke(Refresh);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => { CheckBtn.IsEnabled = FileBtn.IsEnabled = !busy; Refresh(); });
        Refresh();
    }

    private void Refresh()
    {
        var m = Hub.Manifest;
        Included.Text = m == null ? "nessuno" : m.Version.ToString();
        IncludedDetail.Text = m == null ? "Questo programma è stato compilato senza firmware (manca firmware.json)." : $"Build {m.Build} · {m.File}";

        var s = Hub.Status;
        var fw = s.Firmware;
        OnBoard.Text = fw == null ? "—" : fw.IsOriginal ? "originale" : fw.Version.ToString();
        OnBoardDetail.Text = fw == null
            ? (s.Error ?? "Premi «Controlla» con l'adattatore collegato al PC.")
            : fw.IsOriginal ? "Firmware 1.2 senza numero di versione (non risponde a get_version)."
            : $"Build {fw.Build} · porta {s.Port}";

        bool canUpdate = false;
        if (fw == null) { StatusBar.Severity = InfoBarSeverity.Informational; StatusBar.Title = "Adattatore non verificato"; StatusBar.Message = "Collega l'adattatore al PC e premi «Controlla»."; }
        else if (m == null) { StatusBar.Severity = InfoBarSeverity.Informational; StatusBar.Title = "Nessun firmware incluso"; StatusBar.Message = "Usa lo zip completo della build per avere il firmware."; }
        else
        {
            switch (FirmwareCheck.Compare(fw, m))
            {
                case FirmwareStatus.UpdateAvailable:
                    StatusBar.Severity = InfoBarSeverity.Warning;
                    StatusBar.Title = "Aggiornamento disponibile";
                    StatusBar.Message = $"Puoi installare il firmware {m.Version} (build {m.Build}).";
                    canUpdate = true;
                    break;
                case FirmwareStatus.UpToDate:
                    StatusBar.Severity = InfoBarSeverity.Success;
                    StatusBar.Title = "Firmware aggiornato";
                    StatusBar.Message = "Sull'adattatore c'è già il firmware incluso nel programma.";
                    break;
                default:
                    StatusBar.Severity = InfoBarSeverity.Informational;
                    StatusBar.Title = "Firmware più recente";
                    StatusBar.Message = "Sull'adattatore c'è un firmware più recente di quello incluso.";
                    break;
            }
        }
        // Reinstalling is allowed (e.g. to reset everything), the highlight only when an update is available.
        UpdateBtn.IsEnabled = m != null && (fw != null || s.NoAnswer) && !Hub.IsBusy;
        if (fw == null && s.NoAnswer)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = "L'adattatore non risponde";
            StatusBar.Message = $"C'è un ESP32 su {s.Port}, ma non risponde ai comandi: può essere vergine, in modalità download " +
                                "(BOOT premuto) o con un firmware diverso. Puoi installare il firmware incluso o un file.";
            canUpdate = m != null;
            UpdateBtn.Appearance = canUpdate ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
        UpdateBtn.Appearance = canUpdate ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        OpPanel.Begin("Controllo dell'adattatore");
        await _w.CheckAdapterAsync(probeIfSilent: true);
        OpPanel.End(Hub.Status.Error == null ? "Controllo completato" : "Adattatore non raggiungibile");
    }

    private async void File_Click(object sender, RoutedEventArgs e) => await InstallFromFileAsync();

    /// <summary>Full image chosen by the user, always at 0x0 (also used when the adapter is blank).</summary>
    public async Task InstallFromFileAsync()
    {
        if (Hub.IsBusy) return;
        var dlg = new OpenFileDialog { Title = "Scegli il firmware da installare (immagine completa, indirizzo 0x0)", Filter = "Firmware (*.bin)|*.bin|Tutti i file (*.*)|*.*" };
        if (dlg.ShowDialog(_w) != true) return;
        var answer = MessageBox.Show(_w,
            $"Installare {Path.GetFileName(dlg.FileName)} sull'adattatore, all'indirizzo 0x0?\n\n" +
            "⚠ Tutta la memoria dell'adattatore viene cancellata: le impostazioni tornano ai valori predefiniti.\n\n" +
            "Un firmware diverso da questo progetto può non rispondere ai comandi del programma: in quel caso le sezioni " +
            "Gamepad, Mouse, LED e Impostazioni non potranno comunicare con l'adattatore.",
            "Installa firmware da file", MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        OpPanel.Begin($"Installazione di {Path.GetFileName(dlg.FileName)}");
        var r = await Hub.FlashFileAsync(dlg.FileName);
        OpPanel.End(r.Success ? "Firmware installato" : "Installazione non riuscita");
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, r.Success ? "Firmware installato" : "Installazione firmware",
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }

    /// <summary>The included firmware, also used when the adapter is blank.</summary>
    public async Task InstallIncludedAsync(bool confirm)
    {
        var m = Hub.Manifest;
        if (m == null || Hub.IsBusy) return;
        if (confirm && MessageBox.Show(_w,
                $"Installare il firmware {m.Version} sull'adattatore?\n\n⚠ Tutta la memoria dell'adattatore viene cancellata.",
                "Installa firmware", MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        OpPanel.Begin("Installazione del firmware");
        var r = await Hub.FlashAsync();
        OpPanel.End(r.Success ? "Firmware installato" : "Installazione non riuscita");
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, r.Success ? "Firmware installato" : "Installazione firmware",
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var m = Hub.Manifest;
        if (m == null || Hub.IsBusy) return;
        var answer = MessageBox.Show(_w,
            $"Installare il firmware {m.Version} sull'adattatore?\n\n" +
            "⚠ L'aggiornamento cancella TUTTE le impostazioni salvate sull'adattatore e lo riporta ai valori predefiniti, " +
            "come «erase all» dalla pagina web: profilo pad, impostazioni mouse e LED, Developer Mode, inversione dello switch.\n\n" +
            "Non scollegare l'adattatore durante l'aggiornamento. Deve essere collegato solo al PC, non al C64/Amiga.",
            "Aggiorna firmware", MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        OpPanel.Begin("Aggiornamento del firmware");
        var r = await Hub.FlashAsync();
        OpPanel.End(r.Success ? "Firmware aggiornato" : "Aggiornamento non riuscito");
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, r.Success ? "Firmware aggiornato" : "Aggiornamento firmware",
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }
}
