using System.Windows;
using System.Windows.Controls;
using UsbToC64.App.Services;

namespace UsbToC64.App.Controls;

/// <summary>Live view of the serial exchange of the operation started from this page.</summary>
public partial class OperationPanel : UserControl
{
    private bool _active;

    public OperationPanel() => InitializeComponent();

    public void Attach(AdapterHub hub) =>
        hub.LogLine += line => Dispatcher.BeginInvoke(() =>
        {
            if (!_active) return;
            Box.AppendText(line + Environment.NewLine);
            Box.ScrollToEnd();
        });

    public void Begin(string title)
    {
        _active = true;
        Box.Clear();
        HeaderText.Text = title + "…";
        Ring.Visibility = Visibility.Visible;
        Exp.IsExpanded = true;
    }

    public void End(string result)
    {
        _active = false;
        HeaderText.Text = result;
        Ring.Visibility = Visibility.Collapsed;
    }
}
