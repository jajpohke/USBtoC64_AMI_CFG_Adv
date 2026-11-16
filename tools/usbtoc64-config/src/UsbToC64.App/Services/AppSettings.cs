using System.Text.Json;

namespace UsbToC64.App.Services;

/// <summary>Program preferences in %APPDATA%\USBtoC64Config\settings.json.</summary>
public sealed class AppSettings
{
    public bool StartAdvanced { get; set; }

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
