using System.Windows.Controls;

namespace UsbToC64.App.Pages;

public partial class ComingSoonPage : UserControl
{
    public ComingSoonPage(string title, string description)
    {
        InitializeComponent();
        TitleText.Text = title;
        DescText.Text = description;
    }
}
