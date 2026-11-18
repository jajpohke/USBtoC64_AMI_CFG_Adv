using System.Text.Json;

namespace UsbToC64.App.Services;

/// <summary>Program preferences in %APPDATA%\USBtoC64Config\settings.json.</summary>
public sealed class AppSettings
{
    public bool StartAdvanced { get; set; }

    /// <summary>Details of the progress panel open (default: closed, a link opens them).</summary>
    public bool ShowProgressDetails { get; set; }

    /// <summary>"en", "it", "es", "de"; null = English (default).</summary>
    public string? Language { get; set; }

    private static AppSettings? _current;
    /// <summary>The preferences of this run, loaded once.</summary>
    public static AppSettings Current => _current ??= Load();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "USBtoC64Config", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { /* unreadable: defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { /* not writable: the choice is just not remembered */ }
    }
}
