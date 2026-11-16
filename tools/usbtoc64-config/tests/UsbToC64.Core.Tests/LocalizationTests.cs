using System.Text.RegularExpressions;
using UsbToC64.App;
using UsbToC64.Cli;
using UsbToC64.Core.Text;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>
/// The four languages stay complete: every key used in the sources exists, no text is missing,
/// and every translation has the same placeholders as the English one.
/// </summary>
[Collection("Language")]
public class LocalizationTests
{
    private static List<Tr> AllEntries() => CoreStrings.All.Concat(AppStrings.All).Concat(CliStrings.All).ToList();

    [Fact]
    public void Keys_AreUnique()
    {
        var dup = AllEntries().GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Chiavi duplicate: " + string.Join(", ", dup));
    }

    [Fact]
    public void EveryLanguage_HasEveryText()
    {
        var missing = AllEntries()
            .Where(e => new[] { e.En, e.It, e.Es, e.De }.Any(string.IsNullOrWhiteSpace))
            .Select(e => e.Key).ToList();
        Assert.True(missing.Count == 0, "Traduzioni mancanti: " + string.Join(", ", missing));
    }

    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[,:][^}]*)?\}");

    private static string Holes(string s) =>
        string.Join(",", Placeholder.Matches(s).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));

    [Fact]
    public void Placeholders_MatchEnglish()
    {
        var bad = new List<string>();
        foreach (var e in AllEntries())
        {
            var en = Holes(e.En);
            foreach (var (lang, text) in new[] { ("it", e.It), ("es", e.Es), ("de", e.De) })
                if (Holes(text) != en) bad.Add($"{e.Key} [{lang}]");
            // A format string must be valid for string.Format.
            foreach (var text in new[] { e.En, e.It, e.Es, e.De })
            {
                try { _ = string.Format(text, Enumerable.Range(0, 10).Select(i => (object)i).ToArray()); }
                catch (FormatException) { bad.Add($"{e.Key} (formato non valido)"); }
            }
        }
        Assert.True(bad.Count == 0, "Segnaposto diversi dall'inglese: " + string.Join(", ", bad));
    }

    private static string SourceRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "UsbToC64.Core"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src");
    }

    private static readonly Regex XamlKey = new(@"\{l:T\s+(?:Key=)?([A-Za-z0-9_.]+)\s*\}");
    private static readonly Regex CallStart = new(@"\bL\.(?:T|F)\(");
    private static readonly Regex Literal = new(@"""([a-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)""");

    /// <summary>Keys in L.T(...)/L.F(...) first arguments (also "a" : "b" choices) and in {l:T key}.</summary>
    private static IEnumerable<(string File, string Key)> UsedKeys()
    {
        var root = SourceRoot();
        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(f);
            if (f.EndsWith(".xaml"))
                foreach (Match m in XamlKey.Matches(text)) yield return (f, m.Groups[1].Value);
            else if (f.EndsWith(".cs"))
                foreach (Match m in CallStart.Matches(text))
                {
                    // First argument: up to the top-level ',' or the closing ')'.
                    int depth = 0, i = m.Index + m.Length;
                    var sb = new System.Text.StringBuilder();
                    bool inStr = false;
                    for (; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == '"' && (i == 0 || text[i - 1] != '\\')) inStr = !inStr;
                        if (!inStr)
                        {
                            if (c is '(' or '[' or '{') depth++;
                            else if (c is ')' or ']' or '}') { if (depth == 0) break; depth--; }
                            else if (c == ',' && depth == 0) break;
                        }
                        sb.Append(c);
                    }
                    foreach (Match k in Literal.Matches(sb.ToString())) yield return (f, k.Groups[1].Value);
                }
        }
    }

    [Fact]
    public void EveryKeyUsedInTheSources_Exists()
    {
        var keys = AllEntries().Select(e => e.Key).ToHashSet();
        var used = UsedKeys().ToList();
        Assert.NotEmpty(used);
        var missing = used.Where(u => !keys.Contains(u.Key))
            .Select(u => $"{u.Key} ({Path.GetFileName(u.File)})").Distinct().ToList();
        Assert.True(missing.Count == 0, "Chiavi senza testo: " + string.Join(", ", missing));
    }

    [Fact]
    public void Language_DefaultsToEnglish_AndSwitches()
    {
        L.Add(AppStrings.All);
        try
        {
            L.Set(null);
            Assert.Equal("en", L.Current);
            Assert.Equal("Save to USBtoC64", L.T("simple.save"));
            L.Set("it-IT");
            Assert.Equal("Salva su USBtoC64", L.T("simple.save"));
            L.Set("Deutsch");
            Assert.Equal("de", L.Current);
            L.Set("fr");
            Assert.Equal("en", L.Current);
            Assert.Equal("no.such.key", L.T("no.such.key"));
        }
        finally { L.Set("en"); }
    }
}

/// <summary>Tests that change the program language run alone, so that other tests always see English.</summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public class LanguageCollection { }
