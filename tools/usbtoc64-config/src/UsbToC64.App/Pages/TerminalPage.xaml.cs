using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UsbToC64.App.Services;
using UsbToC64.Core.Text;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

/// <summary>Interactive serial terminal: the firmware service menu (info, dumppad, …) and JSON commands.</summary>
public partial class TerminalPage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private readonly List<string> _history = new();
    private int _historyPos;
    private readonly StringBuilder _text = new();

    private static readonly (string Cmd, bool Confirm)[] QuickCommands =
    {
        ("service", false), ("info", false), ("dumppad", false), ("ledtest", false), ("exit", false),
        ("reboot", true),
    };

    public TerminalPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        foreach (var (cmd, confirm) in QuickCommands)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = cmd, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 3, 12, 3),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                Appearance = confirm ? Wpf.Ui.Controls.ControlAppearance.Caution : Wpf.Ui.Controls.ControlAppearance.Secondary,
            };
            b.Click += (_, _) => Send(cmd);
            Quick.Children.Add(b);
        }
        Hub.TerminalLine += line => Dispatcher.BeginInvoke(() => Append(line));
        Hub.TerminalStateChanged += () => Dispatcher.BeginInvoke(UpdateState);
        // Operations started from the other sections appear here too, so the whole exchange is in one place.
        Hub.LogLine += line => Dispatcher.BeginInvoke(() => { if (Hub.TerminalConnected || Hub.IsBusy) Append("· " + line); });
        L.Changed += () => Dispatcher.BeginInvoke(UpdateState);
        UpdateState();
    }

    private void Append(string line)
    {
        _text.AppendLine(line);
        Output.AppendText(line + Environment.NewLine);
        Output.ScrollToEnd();
    }

    private void UpdateState()
    {
        bool on = Hub.TerminalConnected;
        StateText.Text = on ? L.F("term.state.connected", Hub.TerminalPort) : L.T("term.state.disconnected");
        ConnectBtn.Content = L.T(on ? "term.disconnect" : "term.connect");
        ConnectBtn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = on ? Wpf.Ui.Controls.SymbolRegular.PlugDisconnected24 : Wpf.Ui.Controls.SymbolRegular.PlugConnected24 };
        // Formatted (not {l:T}) because the text contains literal braces.
        Input.PlaceholderText = L.F("term.input.placeholder");
        // The input is always available: sending connects the terminal if needed.
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Hub.TerminalConnected) Hub.DisconnectTerminal();
            else Hub.ConnectTerminal();
        }
        catch (Exception ex) { Append(L.F("term.connectFailed", ex.Message)); }
        UpdateState();
        if (Hub.TerminalConnected) Input.Focus();
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send(Input.Text);

    private void Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (!Hub.TerminalConnected)
        {
            try { Hub.ConnectTerminal(); UpdateState(); }
            catch (Exception ex) { Append(L.F("term.connectFailed", ex.Message)); return; }
        }
        if (!Confirm(text)) return;
        try { Hub.SendTerminal(text); }
        catch (Exception ex) { Append(L.F("term.sendFailed", ex.Message)); }
        if (_history.Count == 0 || _history[^1] != text) _history.Add(text);
        _historyPos = _history.Count;
        Input.Clear();
    }

    /// <summary>Commands that change the state of the adapter ask first.</summary>
    private bool Confirm(string cmd)
    {
        string c = cmd.ToLowerInvariant();
        string? msg = null;
        if (c.StartsWith("devmode on"))
            msg = L.T("term.confirm.devmode");
        else if (c == "reboot")
            msg = L.T("term.confirm.reboot");
        else if (c == "flash")
            msg = L.T("term.confirm.flash");
        else if (c.Contains("factory_reset") || c.Contains("clear_mem"))
            msg = L.T("term.confirm.erase");
        return msg == null || MessageBox.Show(_w, msg, L.T("term.confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Send(Input.Text); e.Handled = true; }
        else if (e.Key == Key.Up && _history.Count > 0)
        {
            _historyPos = Math.Max(0, _historyPos - 1);
            Input.Text = _history[_historyPos]; Input.CaretIndex = Input.Text.Length; e.Handled = true;
        }
        else if (e.Key == Key.Down && _history.Count > 0)
        {
            _historyPos = Math.Min(_history.Count, _historyPos + 1);
            Input.Text = _historyPos < _history.Count ? _history[_historyPos] : "";
            Input.CaretIndex = Input.Text.Length; e.Handled = true;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) { Output.Clear(); _text.Clear(); }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = L.T("term.save.filter"), FileName = $"usbtoc64-terminale-{DateTime.Now:yyyyMMdd-HHmm}.txt" };
        if (dlg.ShowDialog(_w) == true) File.WriteAllText(dlg.FileName, _text.ToString());
    }
}
