using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UsbToC64.App.Services;
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
        StateText.Text = on ? $"● Collegato · {Hub.TerminalPort} · 115200" : "Scollegato";
        ConnectBtn.Content = on ? "Disconnetti" : "Connetti";
        ConnectBtn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = on ? Wpf.Ui.Controls.SymbolRegular.PlugDisconnected24 : Wpf.Ui.Controls.SymbolRegular.PlugConnected24 };
        // The input is always available: sending connects the terminal if needed.
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Hub.TerminalConnected) Hub.DisconnectTerminal();
            else Hub.ConnectTerminal();
        }
        catch (Exception ex) { Append($"(impossibile collegarsi: {ex.Message})"); }
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
            catch (Exception ex) { Append($"(impossibile collegarsi: {ex.Message})"); return; }
        }
        if (!Confirm(text)) return;
        try { Hub.SendTerminal(text); }
        catch (Exception ex) { Append($"(invio non riuscito: {ex.Message})"); }
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
            msg = "Con «devmode on» l'adattatore si riavvia in Developer Mode e la seriale passa ai pin 43/44: dalla USB-C " +
                  "il terminale non riceverà più niente. Per tornare indietro serve la seriale sui pin 43/44 («devmode off»).\n\nInviare comunque?";
        else if (c == "reboot")
            msg = "L'adattatore si riavvia. Il terminale si ricollega da solo appena torna disponibile.\n\nInviare?";
        else if (c == "flash")
            msg = "Il comando «flash» del firmware cambia lo stato della scheda. Per aggiornare il firmware usa la sezione Firmware.\n\nInviare comunque?";
        else if (c.Contains("factory_reset") || c.Contains("clear_mem"))
            msg = "Il comando cancella impostazioni salvate sull'adattatore.\n\nInviare comunque?";
        return msg == null || MessageBox.Show(_w, msg, "Conferma comando", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
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
        var dlg = new SaveFileDialog { Filter = "Testo (*.txt)|*.txt", FileName = $"usbtoc64-terminale-{DateTime.Now:yyyyMMdd-HHmm}.txt" };
        if (dlg.ShowDialog(_w) == true) File.WriteAllText(dlg.FileName, _text.ToString());
    }
}
