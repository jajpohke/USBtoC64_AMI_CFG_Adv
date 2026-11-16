using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using UsbToC64.Core.Text;

namespace UsbToC64.App.Loc;

/// <summary>Bindable source of the translated texts: the indexer is refreshed when the language changes.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource()
    {
        L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public string this[string key] => L.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>XAML: <c>Text="{l:T simple.title}"</c>. The text follows the language chosen at run time.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var b = new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
        return b.ProvideValue(serviceProvider);
    }
}
