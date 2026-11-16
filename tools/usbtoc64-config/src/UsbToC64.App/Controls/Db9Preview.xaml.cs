using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.Core.Emulation;

namespace UsbToC64.App.Controls;

/// <summary>What the C64/Amiga receives on the DB9 port: directions, three fire buttons, autofire.</summary>
public partial class Db9Preview : UserControl
{
    private static readonly Brush DirOn = new SolidColorBrush(Color.FromRgb(0x2F, 0x8B, 0xE0));
    private static readonly Brush Fire1On = new SolidColorBrush(Color.FromRgb(0x2E, 0xAF, 0x50));
    private static readonly Brush Fire2On = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly Brush Fire3On = new SolidColorBrush(Color.FromRgb(0x00, 0xAC, 0xC1));
    private static readonly Brush AutoOn = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

    private C64State? _last;
    private bool _lastLive;

    public Db9Preview() => InitializeComponent();

    public void Update(C64State? state, bool live)
    {
        if (Nullable.Equals(state, _last) && live == _lastLive) return;
        _last = state; _lastLive = live;
        var s = live ? state ?? default : default;
        Set(Up, s.Up, DirOn); Set(Down, s.Down, DirOn); Set(Left, s.Left, DirOn); Set(Right, s.Right, DirOn);
        Set(F1, s.Fire1, Fire1On); Set(F2, s.Fire2, Fire2On); Set(F3, s.Fire3, Fire3On);
        Set(Auto, s.Autofire, AutoOn);
        AutoText.Text = s.Autofire ? "AUTOFIRE ATTIVO" : "AUTOFIRE";
        Opacity = live ? 1.0 : 0.55;
    }

    /// <summary>Fire 2/3 travel on different lines depending on the mode detected by the adapter.</summary>
    public void SetMode(bool? amiga) => ModeNote.Text = amiga switch
    {
        true => "Modalità Amiga: Fire 2 e Fire 3 sono i pulsanti 2 e 3 (pin 9 e 5).",
        false => "Modalità C64: Fire 2 e Fire 3 passano dalle linee dei paddle (POT X/POT Y).",
        null => "",
    };

    private void Set(Border b, bool on, Brush onBrush)
    {
        if (on) b.Background = onBrush; else b.ClearValue(Border.BackgroundProperty);
        if (b.Child is TextBlock t)
        {
            if (on) t.Foreground = Brushes.White; else t.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
