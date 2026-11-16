using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Text;
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
        L.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => { CheckBtn.IsEnabled = FileBtn.IsEnabled = !busy; Refresh(); });
        Refresh();
    }

    private void Refresh()
    {
        var m = Hub.Manifest;
        Included.Text = m == null ? L.T("fwp.none") : m.Version.ToString();
        IncludedDetail.Text = m == null ? L.T("fwp.noManifest") : L.F("fwp.buildFile", m.Build, m.File);

        var s = Hub.Status;
        var fw = s.Firmware;
        OnBoard.Text = fw == null ? "—" : fw.IsOriginal ? L.T("fwp.original") : fw.Version.ToString();
        OnBoardDetail.Text = fw == null
            ? (s.Error ?? L.T("fwp.pressCheck"))
            : fw.IsOriginal ? L.T("fwp.originalDetail")
            : L.F("fwp.buildPort", fw.Build, s.Port);

        bool canUpdate = false;
        if (fw == null) { StatusBar.Severity = InfoBarSeverity.Informational; StatusBar.Title = L.T("fwp.st.unchecked.title"); StatusBar.Message = L.T("fwp.st.unchecked.msg"); }
        else if (m == null) { StatusBar.Severity = InfoBarSeverity.Informational; StatusBar.Title = L.T("fwp.st.noFw.title"); StatusBar.Message = L.T("fwp.st.noFw.msg"); }
        else
        {
            switch (FirmwareCheck.Compare(fw, m))
            {
                case FirmwareStatus.UpdateAvailable:
                    StatusBar.Severity = InfoBarSeverity.Warning;
                    StatusBar.Title = L.T("fwp.st.update.title");
                    StatusBar.Message = L.F("fwp.st.update.msg", m.Version, m.Build);
                    canUpdate = true;
                    break;
                case FirmwareStatus.OtherBuild:
                    StatusBar.Severity = InfoBarSeverity.Informational;
                    StatusBar.Title = L.T("fwp.st.otherBuild.title");
                    StatusBar.Message = L.F("fwp.st.otherBuild.msg", fw.Version, fw.Build, m.Build);
                    break;
                case FirmwareStatus.UpToDate:
                    StatusBar.Severity = InfoBarSeverity.Success;
                    StatusBar.Title = L.T("fwp.st.upToDate.title");
                    StatusBar.Message = L.T("fwp.st.upToDate.msg");
                    break;
                default:
                    StatusBar.Severity = InfoBarSeverity.Informational;
                    StatusBar.Title = L.T("fwp.st.newer.title");
                    StatusBar.Message = L.T("fwp.st.newer.msg");
                    break;
            }
        }
        // Reinstalling is allowed (e.g. to reset everything), the highlight only when an update is available.
        UpdateBtn.IsEnabled = m != null && (fw != null || s.NoAnswer) && !Hub.IsBusy;
        if (fw == null && s.NoAnswer)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = L.T("fwp.st.noAnswer.title");
            StatusBar.Message = L.F("fwp.st.noAnswer.msg", s.Port);
            canUpdate = m != null;
            UpdateBtn.Appearance = canUpdate ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
        UpdateBtn.Appearance = canUpdate ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        OpPanel.Begin(L.T("fwp.op.check"));
        await _w.CheckAdapterAsync(probeIfSilent: true);
        OpPanel.End(L.T(Hub.Status.Error == null ? "fwp.op.checkDone" : "fwp.op.unreachable"));
    }

    private async void File_Click(object sender, RoutedEventArgs e) => await InstallFromFileAsync();

    /// <summary>Full image chosen by the user, always at 0x0 (also used when the adapter is blank).</summary>
    public async Task InstallFromFileAsync()
    {
        if (Hub.IsBusy) return;
        var dlg = new OpenFileDialog { Title = L.T("fwp.file.dialogTitle"), Filter = L.T("fwp.file.filter") };
        if (dlg.ShowDialog(_w) != true) return;
        var answer = MessageBox.Show(_w,
            L.F("fwp.file.confirm", Path.GetFileName(dlg.FileName)),
            L.T("fwp.file.confirmTitle"), MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        OpPanel.Begin(L.F("fwp.op.installing", Path.GetFileName(dlg.FileName)));
        var r = await Hub.FlashFileAsync(dlg.FileName);
        OpPanel.End(L.T(r.Success ? "fwp.installed" : "fwp.installFailed"));
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, L.T(r.Success ? "fwp.installed" : "fwp.installTitle"),
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }

    /// <summary>The included firmware, also used when the adapter is blank.</summary>
    public async Task InstallIncludedAsync(bool confirm)
    {
        var m = Hub.Manifest;
        if (m == null || Hub.IsBusy) return;
        if (confirm && MessageBox.Show(_w,
                L.F("fwp.included.confirm", m.Version),
                L.T("fwp.included.confirmTitle"), MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        OpPanel.Begin(L.T("fwp.op.installIncluded"));
        var r = await Hub.FlashAsync();
        OpPanel.End(L.T(r.Success ? "fwp.installed" : "fwp.installFailed"));
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, L.T(r.Success ? "fwp.installed" : "fwp.installTitle"),
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var m = Hub.Manifest;
        if (m == null || Hub.IsBusy) return;
        var answer = MessageBox.Show(_w,
            L.F("fwp.update.confirm", m.Version),
            L.T("fwp.update.title"), MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        OpPanel.Begin(L.T("fwp.op.update"));
        var r = await Hub.FlashAsync();
        OpPanel.End(L.T(r.Success ? "fwp.updated" : "fwp.updateFailed"));
        await _w.CheckAdapterAsync(probeIfSilent: false);
        _w.RefreshFirmwareBar();
        MessageBox.Show(_w, r.Message, L.T(r.Success ? "fwp.updated" : "fwp.updateTitle"),
            MessageBoxButton.OK, r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
    }
}
