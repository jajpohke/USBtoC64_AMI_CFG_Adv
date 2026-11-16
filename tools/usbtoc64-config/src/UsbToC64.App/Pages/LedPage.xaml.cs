using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Text;
using MessageBox = System.Windows.MessageBox;

namespace UsbToC64.App.Pages;

public sealed record LedColor(string Hex, string Name, Brush Brush);

public sealed class LedSlotRow : INotifyPropertyChanged
{
    private LedColor _selected;
    public LedSlotRow(LedSlot slot, IReadOnlyList<LedColor> palette, string hex)
    {
        Slot = slot; Palette = palette;
        _selected = Find(hex);
    }
    public LedSlot Slot { get; }
    public string Label => Slot.Label;
    public IReadOnlyList<LedColor> Palette { get; }
    public LedColor Selected { get => _selected; set { _selected = value; OnChanged(); } }
    public void Set(string hex) => Selected = Find(hex);
    private LedColor Find(string hex) =>
        Palette.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)) ?? Palette[^1];
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public partial class LedPage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private List<LedSlotRow> _rows = new();
    private bool _loadedOnce;

    public LedPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        BuildRows();
        Show(LedSettings.Default);
        // Event and colour names come from the Core: rebuild the rows (keeping the choices) when the language changes.
        L.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            var current = _rows.ToDictionary(r => r.Slot.Key, r => r.Selected.Hex);
            BuildRows();
            foreach (var r in _rows) r.Set(current[r.Slot.Key]);
        });
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => LoadBtn.IsEnabled = SaveBtn.IsEnabled = ResetBtn.IsEnabled = !busy);
        IsVisibleChanged += async (_, e) => { if ((bool)e.NewValue && !_loadedOnce && Hub.Status.Info != null) await LoadAsync(); };
    }

    private void BuildRows()
    {
        var palette = LedSettings.Palette
            .Select(p => new LedColor(p.Hex, p.Name, new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Hex))))
            .ToList();
        _rows = LedSettings.Slots.Select(s => new LedSlotRow(s, palette, LedSettings.Default.Colors[s.Key])).ToList();
        Slots.ItemsSource = _rows;
    }

    private void Show(LedSettings l)
    {
        foreach (var r in _rows) r.Set(l.Colors[r.Slot.Key]);
        Format.SelectedIndex = l.Format == 1 ? 1 : 0;
        Mono.IsChecked = !l.Multicolor;
    }

    private LedSettings Current() =>
        new(_rows.ToDictionary(r => r.Slot.Key, r => r.Selected.Hex), Format.SelectedIndex == 1 ? 1 : 0, Mono.IsChecked != true);

    private async Task LoadAsync()
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin(L.T("led.op.read"));
        try
        {
            Show(await Hub.SessionAsync(L.T("led.session.read"), s => s.GetLed()));
            _loadedOnce = true;
            OpPanel.End(L.T("led.read.done"));
            Result.Text = L.F("led.readAt", DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex) { OpPanel.End(L.T("led.read.failed")); Result.Text = ex.Message; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        var l = Current();
        OpPanel.Begin(L.T("led.op.save"));
        try
        {
            await Hub.SessionAsync(L.T("led.session.save"), s => { s.SaveLed(l); return true; });
            OpPanel.End(L.T("led.save.done"));
            Result.Text = L.F("led.savedAt", DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex) { OpPanel.End(L.T("led.save.failed")); Result.Text = ex.Message; }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        if (MessageBox.Show(_w, L.T("led.reset.confirm"),
                L.T("led.reset.title"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        OpPanel.Begin(L.T("led.op.reset"));
        try
        {
            await Hub.SessionAsync(L.T("led.session.reset"), s => { s.ResetLed(); return true; });
            Show(LedSettings.Default);
            OpPanel.End(L.T("led.reset.done"));
            Result.Text = L.T("led.reset.result");
        }
        catch (Exception ex) { OpPanel.End(L.T("led.reset.failed")); Result.Text = ex.Message; }
        await Task.Delay(4000);
        await _w.CheckAdapterAsync();
    }
}
