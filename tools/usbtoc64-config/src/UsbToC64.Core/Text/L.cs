using System.Globalization;

namespace UsbToC64.Core.Text;

/// <summary>One translated text: English, Italian, Spanish, German. Placeholders are string.Format ones ({0}, {1}…).</summary>
public sealed record Tr(string Key, string En, string It, string Es, string De)
{
    public string For(string lang) => lang switch { "it" => It, "es" => Es, "de" => De, _ => En };
}

/// <summary>
/// Program texts in the four languages. Tables are registered by each project (Core registers its own here,
/// the App and the CLI call <see cref="Add"/> at start-up). The default language is English.
/// </summary>
public static class L
{
    public static readonly string[] Languages = { "en", "it", "es", "de" };
    public static readonly string[] NativeNames = { "English", "Italiano", "Español", "Deutsch" };

    private static readonly Dictionary<string, Tr> Table = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    static L() => Add(CoreStrings.All);

    public static string Current { get; private set; } = "en";

    /// <summary>Raised after the language changes.</summary>
    public static event Action? Changed;

    public static void Add(IEnumerable<Tr> entries)
    {
        lock (Gate)
            foreach (var e in entries) Table[e.Key] = e;
    }

    /// <summary>"it", "it-IT", "Italiano"… → "it"; unknown or empty → "en".</summary>
    public static string Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "en";
        var s = lang.Trim();
        int i = Array.FindIndex(NativeNames, n => n.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return Languages[i];
        s = s.Split('-', '_')[0].ToLowerInvariant();
        return Languages.Contains(s) ? s : "en";
    }

    public static void Set(string? lang)
    {
        var n = Normalize(lang);
        if (n == Current) return;
        Current = n;
        Changed?.Invoke();
    }

    /// <summary>Translated text; falls back to English, then to the key itself.</summary>
    public static string T(string key)
    {
        Tr? e;
        lock (Gate) Table.TryGetValue(key, out e);
        if (e == null) return key;
        var s = e.For(Current);
        return string.IsNullOrEmpty(s) ? e.En : s;
    }

    /// <summary>Translated text with placeholders filled in (invariant number format).</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(key), args);

    /// <summary>All registered entries (used by the tests).</summary>
    public static IReadOnlyCollection<Tr> Entries
    {
        get { lock (Gate) return Table.Values.ToList(); }
    }
}
