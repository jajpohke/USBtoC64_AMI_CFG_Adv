using System.Windows.Controls;
using UsbToC64.App.Services;
using UsbToC64.Core.Text;

namespace UsbToC64.App.Controls;

/// <summary>Program language (English, Italiano, Español, Deutsch): applied at once and remembered.</summary>
public partial class LanguagePicker : UserControl
{
    private bool _filling;

    public LanguagePicker()
    {
        InitializeComponent();
        foreach (var n in L.NativeNames) Combo.Items.Add(n);
        Sync();
        L.Changed += () => Dispatcher.BeginInvoke(Sync);
    }

    private void Sync()
    {
        _filling = true;
        Combo.SelectedIndex = Array.IndexOf(L.Languages, L.Current);
        _filling = false;
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Combo.SelectedIndex < 0) return;
        var lang = L.Languages[Combo.SelectedIndex];
        AppSettings.Current.Language = lang;
        AppSettings.Current.Save();
        L.Set(lang);
    }
}
