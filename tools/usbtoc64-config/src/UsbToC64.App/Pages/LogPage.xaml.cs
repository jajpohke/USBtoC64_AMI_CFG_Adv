using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace UsbToC64.App.Pages;

public partial class LogPage : UserControl
{
    private readonly MainWindow _w;

    public LogPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        _w.Hub.LogLine += line => Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(_w.Hub.LogText());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Testo (*.txt)|*.txt", FileName = $"usbtoc64-registro-{DateTime.Now:yyyyMMdd-HHmm}.txt" };
        if (dlg.ShowDialog(_w) == true) File.WriteAllText(dlg.FileName, _w.Hub.LogText());
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _w.Hub.ClearLog();
        LogBox.Clear();
    }
}
