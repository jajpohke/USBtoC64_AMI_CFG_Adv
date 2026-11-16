using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
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
    private readonly List<LedSlotRow> _rows;
    private bool _loadedOnce;

    public LedPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        var palette = LedSettings.Palette
            .Select(p => new LedColor(p.Hex, p.Name, new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Hex))))
            .ToList();
        _rows = LedSettings.Slots.Select(s => new LedSlotRow(s, palette, LedSettings.Default.Colors[s.Key])).ToList();
        Slots.ItemsSource = _rows;
        Show(LedSettings.Default);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => LoadBtn.IsEnabled = SaveBtn.IsEnabled = ResetBtn.IsEnabled = !busy);
        IsVisibleChanged += async (_, e) => { if ((bool)e.NewValue && !_loadedOnce && Hub.Status.Info != null) await LoadAsync(); };
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
        OpPanel.Begin("Lettura dei colori LED");
        try
        {
            Show(await Hub.SessionAsync("Lettura impostazioni LED", s => s.GetLed()));
            _loadedOnce = true;
            OpPanel.End("Colori letti");
            Result.Text = $"Letti dall'adattatore alle {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex) { OpPanel.End("Lettura non riuscita"); Result.Text = ex.Message; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        var l = Current();
        OpPanel.Begin("Salvataggio dei colori LED");
        try
        {
            await Hub.SessionAsync("Salvataggio impostazioni LED", s => { s.SaveLed(l); return true; });
            OpPanel.End("Colori salvati");
            Result.Text = $"Salvati sull'adattatore alle {DateTime.Now:HH:mm}: il LED li usa subito.";
        }
        catch (Exception ex) { OpPanel.End("Salvataggio non riuscito"); Result.Text = ex.Message; }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        if (MessageBox.Show(_w, "Ripristinare i colori e il formato LED predefiniti? L'adattatore si riavvia.",
                "Ripristina predefiniti", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        OpPanel.Begin("Ripristino dei colori predefiniti");
        try
        {
            await Hub.SessionAsync("Ripristino impostazioni LED", s => { s.ResetLed(); return true; });
            Show(LedSettings.Default);
            OpPanel.End("Colori predefiniti ripristinati");
            Result.Text = "Colori predefiniti ripristinati. L'adattatore si sta riavviando.";
        }
        catch (Exception ex) { OpPanel.End("Ripristino non riuscito"); Result.Text = ex.Message; }
        await Task.Delay(4000);
        await _w.CheckAdapterAsync();
    }
}
