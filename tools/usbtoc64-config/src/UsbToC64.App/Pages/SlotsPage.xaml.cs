using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsbToC64.App.Dialogs;
using UsbToC64.App.Services;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace UsbToC64.App.Pages;

/// <summary>The 8 profile drawers of the adapter (firmware 1.3.0): edit, rename, delete.</summary>
public partial class SlotsPage : UserControl
{
    private readonly MainWindow _w;
    private AdapterHub Hub => _w.Hub;
    private PadSession Session => _w.Pad;

    public SlotsPage(MainWindow w)
    {
        InitializeComponent();
        _w = w;
        Hub.StatusChanged += () => Dispatcher.BeginInvoke(Fill);
        Hub.BusyChanged += _ => Dispatcher.BeginInvoke(Fill);
        Session.Changed += () => Dispatcher.BeginInvoke(Fill);
        L.Changed += () => Dispatcher.BeginInvoke(Fill);
        Fill();
    }

    private void Fill()
    {
        List.Children.Clear();
        var st = Hub.Status;
        RefreshBtn.IsEnabled = !Hub.IsBusy && st.HasDrawers;
        if (st.Info == null)
        {
            Show(L.T("slots.noAdapter"));
            return;
        }
        if (!st.HasDrawers)
        {
            Show(L.F("slots.needFirmware", FirmwareLimits.SlotsMinFirmware)
                 + (st.StoredPad != null ? " " + L.F("slots.single", $"{st.StoredPad.Vid:X4}:{st.StoredPad.Pid:X4}") : ""));
            return;
        }
        StateText.Visibility = Visibility.Collapsed;
        for (int i = 0; i < FirmwareLimits.MaxSlots; i++)
            List.Children.Add(Row(i, st.Slots!.FirstOrDefault(d => d.Info.Slot == i)));
    }

    private void Show(string text)
    {
        StateText.Text = text;
        StateText.Visibility = Visibility.Visible;
    }

    private UIElement Row(int slot, SlotContent? d)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var dock = new DockPanel();
        var num = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 14, 0) };
        num.SetResourceReference(Border.BackgroundProperty, d == null ? "ControlFillColorSecondaryBrush" : "AccentFillColorDefaultBrush");
        var numText = new TextBlock
        {
            Text = (slot + 1).ToString(), FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        numText.SetResourceReference(TextBlock.ForegroundProperty, d == null ? "TextFillColorSecondaryBrush" : "TextOnAccentFillColorPrimaryBrush");
        num.Child = numText;
        DockPanel.SetDock(num, Dock.Left);
        dock.Children.Add(num);

        if (d != null)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Btn(L.T("slots.edit"), SymbolRegular.Edit24, () => Edit(d), primary: true));
            buttons.Children.Add(Btn(L.T("slots.rename"), SymbolRegular.Rename24, () => _ = Rename(d.Info)));
            buttons.Children.Add(Btn(L.T("slots.delete"), SymbolRegular.Delete24, () => _ = Delete(d.Info)));
            DockPanel.SetDock(buttons, Dock.Right);
            dock.Children.Add(buttons);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = d == null ? L.T("slots.empty") : d.Info.DisplayLabel,
            FontWeight = d == null ? FontWeights.Normal : FontWeights.SemiBold, FontSize = 14,
        };
        if (d == null) title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        text.Children.Add(title);
        if (d != null)
        {
            bool connected = Session.Devices.Any(x => x.Vid == d.Info.Vid && x.Pid == d.Info.Pid);
            bool editing = Session.EditSlot?.Slot == d.Info.Slot;
            var sub = new TextBlock { Margin = new Thickness(0, 2, 0, 0), FontSize = 12 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            sub.Text = L.F("slots.details", d.Info.Ids, d.Info.RuleCount)
                       + (d.Info.Analog ? " · " + L.T("slots.analog") : "")
                       + (d.Info.IsWebConfigured ? " · " + L.T("slots.fromWeb") : "")
                       + (connected ? " · " + L.T("slots.connected") : "")
                       + (editing ? " · " + L.T("slots.editing") : "");
            text.Children.Add(sub);
        }
        dock.Children.Add(text);
        card.Child = dock;
        return card;
    }

    private Button Btn(string text, SymbolRegular icon, Action click, bool primary = false)
    {
        var b = new Button
        {
            Content = text, Icon = new SymbolIcon(icon), Margin = new Thickness(8, 0, 0, 0),
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            IsEnabled = !Hub.IsBusy,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private void Edit(SlotContent d)
    {
        Session.OpenSlot(d);
        _w.Navigate("gamepad");
    }

    private async Task Rename(SlotInfo s)
    {
        var label = SlotDialog.AskRename(_w, s);
        if (label == null) return;
        try
        {
            await Hub.RenameSlotAsync(s.Slot, label);
            LastResult.Text = L.F("slots.renamed", s.Number, label.Length > 0 ? label : SlotLabels.WebConfigured);
        }
        catch (Exception ex) { LastResult.Text = ex.Message; Hub.Log(ex.Message); }
    }

    private async Task Delete(SlotInfo s)
    {
        if (MessageBox.Show(_w, L.F("slots.delete.confirm", s.Number, s.DisplayLabel, s.Ids), L.T("slots.delete.title"),
                MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await Hub.DeleteSlotAsync(s.Slot);
            if (Session.EditSlot?.Slot == s.Slot) Session.CloseSlot();
            LastResult.Text = L.F("slots.deleted", s.Number, s.DisplayLabel);
        }
        catch (Exception ex) { LastResult.Text = ex.Message; Hub.Log(ex.Message); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var list = await Hub.RefreshSlotsAsync();
            LastResult.Text = L.F("slots.refreshed", list.Count, FirmwareLimits.MaxSlots);
        }
        catch (Exception ex) { LastResult.Text = ex.Message; Hub.Log(ex.Message); }
    }
}
