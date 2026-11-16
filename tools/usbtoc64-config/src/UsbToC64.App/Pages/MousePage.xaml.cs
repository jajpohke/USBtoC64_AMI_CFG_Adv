using System.Windows;
using System.Windows.Controls;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;

namespace UsbToC64.App.Pages;

public partial class MousePage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private bool _loadedOnce;

    public MousePage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        OpPanel.Attach(Hub);
        Show(MouseSettings.Default);
        Hub.BusyChanged += busy => Dispatcher.BeginInvoke(() => LoadBtn.IsEnabled = SaveBtn.IsEnabled = !busy);
        // Read the adapter the first time the page is opened.
        IsVisibleChanged += async (_, e) => { if ((bool)e.NewValue && !_loadedOnce && Hub.Status.Info != null) await LoadAsync(); };
    }

    private void Show(MouseSettings m)
    {
        AmigaSpeed.Value = m.AmigaSpeed;
        C64Speed.Value = m.C64Speed;
        Wheel.IsChecked = m.Wheel;
        Timing.SelectedIndex = m.Pal ? 0 : 1;
    }

    private MouseSettings Current() =>
        new((int)AmigaSpeed.Value, (int)C64Speed.Value, Wheel.IsChecked == true, Timing.SelectedIndex != 1);

    private void Speed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AmigaVal == null || C64Val == null) return;
        AmigaVal.Text = ((int)AmigaSpeed.Value).ToString();
        C64Val.Text = ((int)C64Speed.Value).ToString();
    }

    private async Task LoadAsync()
    {
        if (Hub.IsBusy) return;
        OpPanel.Begin("Lettura delle impostazioni del mouse");
        try
        {
            var m = await Hub.SessionAsync("Lettura impostazioni mouse", s => s.GetMouse());
            Show(m);
            _loadedOnce = true;
            OpPanel.End("Impostazioni lette");
            Result.Text = $"Lette dall'adattatore alle {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex)
        {
            OpPanel.End("Lettura non riuscita");
            Result.Text = ex.Message;
        }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Default_Click(object sender, RoutedEventArgs e)
    {
        Show(MouseSettings.Default);
        Result.Text = "Valori predefiniti impostati: premi «Salva sull'adattatore» per applicarli.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.IsBusy) return;
        var m = Current();
        OpPanel.Begin("Salvataggio delle impostazioni del mouse");
        try
        {
            await Hub.SessionAsync("Salvataggio impostazioni mouse", s => { s.SaveMouse(m); return true; });
            OpPanel.End("Impostazioni salvate");
            Result.Text = $"Salvate sull'adattatore alle {DateTime.Now:HH:mm}: valgono subito, senza riavvio.";
        }
        catch (Exception ex)
        {
            OpPanel.End("Salvataggio non riuscito");
            Result.Text = ex.Message;
        }
    }
}
