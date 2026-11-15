using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;

namespace UsbToC64.Core.Device;

/// <summary>A line-oriented connection to the adapter (USB CDC serial).</summary>
public interface IAdapterLink : IDisposable
{
    string Name { get; }
    void SendLine(string line);
    /// <summary>Next complete line (without the newline), or null on timeout.</summary>
    string? ReadLine(TimeSpan timeout);
    void DiscardInput();
    /// <summary>True once the port has gone away (e.g. the adapter rebooted while the link was open).</summary>
    bool Lost { get; }
}

/// <summary>Opens links and tells whether the adapter port is currently present (it disappears while rebooting).</summary>
public interface IAdapterPort
{
    string Name { get; }
    bool IsPresent();
    IAdapterLink Open();
}

public static class ProfileComparer
{
    /// <summary>Differences between what was sent and what the adapter stored. Empty = identical.</summary>
    public static List<string> Compare(JmProfile sent, JmProfile stored)
    {
        var d = new List<string>();
        if (sent.Vid != stored.Vid || sent.Pid != stored.Pid)
            d.Add($"VID/PID: inviato {sent.Vid:X4}:{sent.Pid:X4}, salvato {stored.Vid:X4}:{stored.Pid:X4}");
        if (sent.DpadIndex != stored.DpadIndex)
            d.Add($"dpad_idx: inviato {sent.DpadIndex}, salvato {stored.DpadIndex}");

        var expectedRules = sent.Rules.Take(FirmwareLimits.MaxRules).ToList();
        if (sent.Rules.Count > FirmwareLimits.MaxRules)
            d.Add($"Il firmware tiene solo {FirmwareLimits.MaxRules} regole su {sent.Rules.Count} inviate.");
        if (expectedRules.Count != stored.Rules.Count)
            d.Add($"Numero di regole: inviate {expectedRules.Count}, salvate {stored.Rules.Count}");
        for (int i = 0; i < Math.Min(expectedRules.Count, stored.Rules.Count); i++)
            if (expectedRules[i] != stored.Rules[i])
                d.Add($"Regola {i + 1}: inviata \"{Names.Rule(expectedRules[i])}\", salvata \"{Names.Rule(stored.Rules[i])}\"");

        static string L(IEnumerable<byte> b) => "[" + string.Join(", ", b) + "]";
        var ex = sent.AnalogX.Take(FirmwareLimits.MaxAnalogPerAxis).ToList();
        var ey = sent.AnalogY.Take(FirmwareLimits.MaxAnalogPerAxis).ToList();
        if (!ex.SequenceEqual(stored.AnalogX)) d.Add($"Assi X: inviati {L(ex)}, salvati {L(stored.AnalogX)}");
        if (!ey.SequenceEqual(stored.AnalogY)) d.Add($"Assi Y: inviati {L(ey)}, salvati {L(stored.AnalogY)}");
        return d;
    }
}

/// <summary>Parses the text printed by the service-menu command <c>dumppad</c> (TIER 0 section).</summary>
public static class DumppadParser
{
    private static readonly Dictionary<string, JmFunc> FuncByName =
        Enum.GetValues<JmFunc>().ToDictionary(f => Names.Func(f), f => f);

    private static readonly Dictionary<string, JmOp> OpByName = new() { ["=="] = JmOp.Eq, ["BIT"] = JmOp.BitAny, ["HAT"] = JmOp.Hat };

    /// <returns>The stored profile, or null when the dump has no TIER 0 section.</returns>
    public static JmProfile? Parse(string text)
    {
        int start = text.IndexOf("[ TIER 0", StringComparison.Ordinal);
        if (start < 0) return null;
        var t = text[start..];
        int end = t.IndexOf("[ TIER 1", StringComparison.Ordinal);
        if (end > 0) t = t[..end];

        var p = new JmProfile
        {
            Vid = (ushort)Hex(t, @"vid\s*:\s*0x([0-9A-Fa-f]+)"),
            Pid = (ushort)Hex(t, @"pid\s*:\s*0x([0-9A-Fa-f]+)"),
            DpadIndex = (sbyte)(Regex.Match(t, @"dpad_index\s*:\s*(-?\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1),
        };
        foreach (Match r in Regex.Matches(t, @"Rule\s+\d+\s*:\s*Byte\[(\d+)\]\s*(==|BIT|HAT|\?\?\?)\s*(\d+)\s*->\s*Action:\s*([^\r\n]+)"))
        {
            string opName = r.Groups[2].Value, funcName = r.Groups[4].Value.Trim();
            if (!OpByName.TryGetValue(opName, out var op) || !FuncByName.TryGetValue(funcName, out var f))
                throw new FormatException($"Regola non interpretabile nel dumppad: {r.Value.Trim()}");
            p.Rules.Add(new JmRule(byte.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture),
                                   byte.Parse(r.Groups[3].Value, CultureInfo.InvariantCulture), op, f));
        }
        if (Regex.IsMatch(t, @"use_analog\s*:\s*YES"))
        {
            p.AnalogX.AddRange(List(t, "analog_x"));
            p.AnalogY.AddRange(List(t, "analog_y"));
        }
        return p;
    }

    private static int Hex(string t, string pattern) =>
        Regex.Match(t, pattern) is { Success: true } m ? int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;

    private static IEnumerable<byte> List(string t, string name)
    {
        var m = Regex.Match(t, name + @"\s*:\s*\[([^\]]*)\]");
        if (!m.Success) yield break;
        foreach (var s in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return byte.Parse(s, CultureInfo.InvariantCulture);
    }
}

public enum ReadMethod { GetJm, Dumppad }

public sealed record StoredProfile(JmProfile? Profile, ReadMethod Method, bool HasCustom);

/// <summary>Requests on an open link. JSON replies are recognised by their leading '{'; other lines (boot banner, menu text) are logged.</summary>
public sealed class AdapterSession
{
    private readonly IAdapterLink _link;
    private readonly Action<string> _log;

    public AdapterSession(IAdapterLink link, Action<string>? log = null)
    {
        _link = link;
        _log = log ?? (_ => { });
    }

    public JsonDocument? RequestJson(string json, TimeSpan timeout)
    {
        _link.DiscardInput();
        _log($"→ {json}");
        _link.SendLine(json);
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var line = _link.ReadLine(until - DateTime.UtcNow);
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            _log($"← {line}");
            if (!line.StartsWith('{')) continue;
            try { return JsonDocument.Parse(line); }
            catch (JsonException) { /* not a reply: keep reading */ }
        }
        return null;
    }

    /// <summary>Sends a service-menu text command and collects the output until the line stays quiet.</summary>
    public string RequestText(string command, TimeSpan quiet, TimeSpan max)
    {
        _link.DiscardInput();
        _log($"→ {command}");
        _link.SendLine(command);
        var lines = new List<string>();
        var stop = DateTime.UtcNow + max;
        while (DateTime.UtcNow < stop)
        {
            var line = _link.ReadLine(quiet);
            if (line == null) break;
            lines.Add(line);
        }
        foreach (var l in lines) _log($"← {l}");
        return string.Join("\n", lines);
    }

    /// <summary>Reads the stored TIER 0 profile: get_jm (firmware proto 1) or, on older firmware, the dumppad text.</summary>
    public StoredProfile ReadStored()
    {
        using (var doc = RequestJson("{\"cmd\":\"get_jm\"}", TimeSpan.FromSeconds(2)))
        {
            if (doc != null)
            {
                var root = doc.RootElement;
                string status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                if (status == "empty") return new StoredProfile(null, ReadMethod.GetJm, false);
                if (status != "ok")
                    throw new InvalidOperationException($"L'adattatore ha risposto con un errore: {root.GetRawText()}");
                bool hasCustom = root.TryGetProperty("has_custom", out var hc) && hc.ValueKind == JsonValueKind.True;
                // Same format as save_jm, without "cmd".
                return new StoredProfile(SaveJmJson.Parse(root.GetRawText()), ReadMethod.GetJm, hasCustom);
            }
        }
        _log("(nessuna risposta a get_jm: firmware senza il comando, uso dumppad)");
        var text = RequestText("dumppad", TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(4));
        if (text.Length == 0) throw new TimeoutException("Nessuna risposta né a get_jm né a dumppad.");
        bool has = Regex.IsMatch(text, @"has_custom\s*:\s*TRUE");
        return new StoredProfile(DumppadParser.Parse(text), ReadMethod.Dumppad, has);
    }
}

public sealed record WriteResult(bool Saved, bool Verified, ReadMethod? Method, List<string> Differences, string Message);

/// <summary>save_jm → wait for "ok" → wait for the reboot → read back → compare.</summary>
public sealed class AdapterWriter
{
    private readonly IAdapterPort _port;
    private readonly Action<string> _log;
    private readonly Action<TimeSpan> _sleep;

    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RebootTimeout { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>The firmware waits 2 s after Serial.begin before accepting commands.</summary>
    public TimeSpan BootDelay { get; init; } = TimeSpan.FromSeconds(3);

    public AdapterWriter(IAdapterPort port, Action<string>? log = null, Action<TimeSpan>? sleep = null)
    {
        _port = port;
        _log = log ?? (_ => { });
        _sleep = sleep ?? Thread.Sleep;
    }

    public WriteResult WriteAndVerify(JmProfile profile)
    {
        string json = SaveJmJson.Serialize(profile);

        // 1) send and wait for the reply
        using (var link = _port.Open())
        {
            _log($"Porta {link.Name} aperta.");
            var session = new AdapterSession(link, _log);
            using var reply = session.RequestJson(json, ReplyTimeout);
            if (reply == null)
            {
                // The firmware answers "ok" and reboots 200 ms later: on Windows the port can vanish
                // before the reply is read. The read-back after the reboot decides.
                if (!link.Lost && _port.IsPresent())
                    return new WriteResult(false, false, null, new(),
                        "Nessuna risposta dall'adattatore. Controlla che sia collegato solo al PC (non al C64/Amiga), " +
                        "che la Developer Mode sia spenta e che la porta sia quella giusta.");
                _log("La porta è scomparsa subito dopo l'invio: probabile riavvio dopo il salvataggio. Verifico rileggendo.");
            }
            else
            {
                var root = reply.RootElement;
                string status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                if (status != "ok")
                    return new WriteResult(false, false, null, new(), $"L'adattatore ha rifiutato il profilo: {root.GetRawText()}");
                _log("Profilo accettato. L'adattatore si riavvia…");
            }
        }

        // 2) wait for the reboot: the port disappears and comes back
        var deadline = DateTime.UtcNow + RebootTimeout;
        var goneUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < goneUntil && _port.IsPresent()) _sleep(TimeSpan.FromMilliseconds(100));
        while (DateTime.UtcNow < deadline && !_port.IsPresent()) _sleep(TimeSpan.FromMilliseconds(200));
        if (!_port.IsPresent())
            return new WriteResult(true, false, null, new(),
                "Profilo salvato, ma la porta non è ricomparsa dopo il riavvio: verifica non eseguita.");
        _sleep(BootDelay);

        // 3) read back and compare
        StoredProfile stored;
        try
        {
            using var link = _port.Open();
            _log($"Porta {link.Name} riaperta, rileggo il profilo…");
            stored = new AdapterSession(link, _log).ReadStored();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            return new WriteResult(true, false, null, new(), $"Profilo salvato, ma la rilettura non è riuscita: {ex.Message}");
        }
        if (stored.Profile == null)
            return new WriteResult(true, false, stored.Method, new(), "L'adattatore dice di non avere un profilo salvato dopo il riavvio.");

        var diff = ProfileComparer.Compare(profile, stored.Profile);
        string via = stored.Method == ReadMethod.GetJm ? "get_jm" : "dumppad (firmware senza get_jm)";
        return diff.Count == 0
            ? new WriteResult(true, true, stored.Method, diff, $"Salvato e verificato ({via}): l'adattatore contiene esattamente il profilo inviato.")
            : new WriteResult(true, false, stored.Method, diff, $"Salvato, ma il profilo riletto ({via}) è diverso da quello inviato.");
    }
}
