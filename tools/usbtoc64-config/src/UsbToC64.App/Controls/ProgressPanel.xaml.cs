using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Text;

namespace UsbToC64.App.Controls;

/// <summary>
/// Progress of a firmware installation or a profile save: phase, bar (also on the taskbar icon), a "do not unplug"
/// strip while running, and the read-only details of the serial exchange (closed unless the user opens them).
/// </summary>
public partial class ProgressPanel : UserControl
{
    private bool _active;
    private bool _detailsOpen;
    private OpProgress? _last;
    private bool? _ok;

    /// <summary>Show the details link and box (false where the page already has its own log panel).</summary>
    public bool ShowDetails { get; set; } = true;

    public ProgressPanel()
    {
        InitializeComponent();
        _detailsOpen = AppSettings.Current.ShowProgressDetails;
        L.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    public void Attach(AdapterHub hub) =>
        hub.LogLine += line => Dispatcher.BeginInvoke(() =>
        {
            if (!_active) return;
            Box.AppendText(line + Environment.NewLine);
            Box.ScrollToEnd();
        });

    public void Begin()
    {
        _active = true;
        _ok = null;
        _last = new OpProgress(OpPhase.Connect, null);
        Box.Clear();
        Visibility = Visibility.Visible;
        SetTaskbar(TaskbarItemProgressState.Indeterminate, 0);
        Refresh();
    }

    /// <summary>Thread-safe: called from the operation thread.</summary>
    public void Report(OpProgress p) => Dispatcher.BeginInvoke(() =>
    {
        if (!_active) return;
        _last = p;
        Refresh();
    });

    public void End(bool ok)
    {
        _active = false;
        _ok = ok;
        _last = new OpProgress(OpPhase.Done, ok ? 1.0 : _last?.Overall, null);
        SetTaskbar(ok ? TaskbarItemProgressState.None : TaskbarItemProgressState.Error, _last.Overall ?? 0);
        Refresh();
    }

    /// <summary>Hides the panel (e.g. when the page starts over).</summary>
    public void Reset()
    {
        if (_active) return;
        Visibility = Visibility.Collapsed;
        SetTaskbar(TaskbarItemProgressState.None, 0);
    }

    private void Refresh()
    {
        Warning.Visibility = _active ? Visibility.Visible : Visibility.Collapsed;
        var p = _last;
        PhaseText.Text = _ok switch
        {
            true => "✔ " + L.T("prog.done"),
            false => "✖ " + L.T("prog.failed"),
            null => p == null ? "" : PhaseLabel(p.Phase),
        };
        PercentText.Text = _ok == null && p?.Overall is { } o ? $"{Math.Round(o * 100)} %" : "";
        Bar.IsIndeterminate = _ok == null && p?.Overall == null;
        Bar.Value = (p?.Overall ?? 0) * 100;
        if (_active && p?.Overall is { } v) SetTaskbar(TaskbarItemProgressState.Normal, v);
        else if (_active) SetTaskbar(TaskbarItemProgressState.Indeterminate, 0);

        DetailsBtn.Visibility = CopyBtn.Visibility = ShowDetails ? Visibility.Visible : Visibility.Collapsed;
        DetailsBtn.Content = L.T(_detailsOpen ? "prog.hideDetails" : "prog.showDetails");
        Box.Visibility = ShowDetails && _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
        CopyBtn.Visibility = ShowDetails && _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string PhaseLabel(OpPhase phase) => L.T(phase switch
    {
        OpPhase.Connect => "prog.phase.connect",
        OpPhase.Erase => "prog.phase.erase",
        OpPhase.Write => "prog.phase.write",
        OpPhase.Verify => "prog.phase.verify",
        OpPhase.Restart => "prog.phase.restart",
        OpPhase.Check => "prog.phase.check",
        OpPhase.Send => "prog.phase.send",
        OpPhase.ReadBack => "prog.phase.readback",
        OpPhase.Compare => "prog.phase.compare",
        _ => "prog.done",
    });

    private void SetTaskbar(TaskbarItemProgressState state, double value)
    {
        var w = Window.GetWindow(this);
        if (w == null) return;
        w.TaskbarItemInfo ??= new TaskbarItemInfo();
        w.TaskbarItemInfo.ProgressState = state;
        w.TaskbarItemInfo.ProgressValue = value;
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        _detailsOpen = !_detailsOpen;
        AppSettings.Current.ShowProgressDetails = _detailsOpen;
        AppSettings.Current.Save();
        Refresh();
        if (_detailsOpen) Box.ScrollToEnd();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Box.Text); }
        catch (Exception) { /* clipboard busy */ }
    }
}
