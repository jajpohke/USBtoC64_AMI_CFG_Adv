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
    /// <summary>Called when the last opened link answered: the port can keep using the same settings.</summary>
    void ConnectionSucceeded() { }
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

/// <summary>Reply to get_info (exists in the original firmware too).</summary>
public sealed record AdapterInfo(bool IsAmiga, bool DevMode, bool InvertSwitch, string Raw)
{
    public override string ToString() => $"modalità {(IsAmiga ? "Amiga" : "C64")}, Developer Mode {(DevMode ? "ON" : "OFF")}";
}

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

    /// <summary>Non-JSON lines received during the last request (boot messages, menu text).</summary>
    public List<string> LastNoise { get; } = new();

    /// <summary>True once boot messages were seen on this link (the adapter restarted after the port was opened).</summary>
    public bool SawBoot { get; private set; }

    /// <summary>Lines printed by the ESP32 ROM bootloader or by the firmware banner while it starts.</summary>
    public static bool IsBootLine(string line) =>
        line.StartsWith("ESP-ROM", StringComparison.Ordinal) || line.StartsWith("rst:", StringComparison.Ordinal) ||
        line.StartsWith("load:0x", StringComparison.Ordinal) || line.StartsWith("entry 0x", StringComparison.Ordinal) ||
        line.StartsWith("mode:", StringComparison.Ordinal) || line.StartsWith("SPIWP:", StringComparison.Ordinal) ||
        line.Contains("Booting", StringComparison.Ordinal);

    private static bool IsBannerEnd(string line) => line.Contains("Booting", StringComparison.Ordinal);

    public JsonDocument? RequestJson(string json, TimeSpan timeout)
    {
        _link.DiscardInput();
        LastNoise.Clear();
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
            if (!line.StartsWith('{')) { LastNoise.Add(line); if (IsBootLine(line)) SawBoot = true; continue; }
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

    /// <summary>
    /// Communication test with a command the original firmware already has (get_info, used by the web memory page).
    /// No reply means: wrong port, adapter connected to the C64/Amiga, or Developer Mode on.
    /// </summary>
    public AdapterInfo Ping(TimeSpan? timeout = null)
    {
        var doc = RequestJson("{\"cmd\":\"get_info\"}", timeout ?? TimeSpan.FromSeconds(2));
        if (doc == null && LastNoise.Any(IsBootLine))
        {
            // The adapter restarted (typically when the port was opened): the command was lost.
            // Wait for the firmware banner ("Booting: ..."), printed after its 2 s start-up delay, then retry.
            if (!LastNoise.Any(IsBannerEnd))
            {
                _log("L'adattatore si sta riavviando: attendo la fine dell'avvio…");
                WaitForBanner(TimeSpan.FromSeconds(8));
            }
            if (_link.Lost)
                throw new IOException("La porta si è chiusa durante il riavvio dell'adattatore.");
            Thread.Sleep(300);
            doc = RequestJson("{\"cmd\":\"get_info\"}", timeout ?? TimeSpan.FromSeconds(2));
        }
        using var reply = doc
            ?? throw new TimeoutException(
                "L'adattatore non risponde al comando di prova (get_info). Controlla che sia collegato solo al PC " +
                "(non al C64/Amiga), che la Developer Mode sia spenta e che la porta sia quella giusta.");
        var r = reply.RootElement;
        bool B(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        if (!r.TryGetProperty("is_amiga", out _))
            throw new InvalidOperationException($"Risposta inattesa al comando di prova: {r.GetRawText()}");
        return new AdapterInfo(B("is_amiga"), B("dev_mode"), B("inv_switch"), r.GetRawText());
    }

    private void WaitForBanner(TimeSpan max)
    {
        var until = DateTime.UtcNow + max;
        while (DateTime.UtcNow < until)
        {
            var line = _link.ReadLine(until - DateTime.UtcNow);
            if (line == null) return;
            line = line.Trim();
            if (line.Length == 0) continue;
            _log($"← {line}");
            if (IsBootLine(line)) SawBoot = true;
            if (IsBannerEnd(line)) return;
        }
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

/// <summary>
/// Opens the adapter port and checks the communication. Opening the port can restart the adapter, and at boot the
/// firmware forces a USB reconnection (GPIO 19/20), so the first handle may die: in that case wait for the port to
/// come back, let the firmware start, and open it again — like a serial terminal that reconnects.
/// </summary>
public sealed class AdapterConnection : IDisposable
{
    public IAdapterLink Link { get; }
    public AdapterSession Session { get; }
    public AdapterInfo Info { get; }

    private AdapterConnection(IAdapterLink link, AdapterSession session, AdapterInfo info)
    {
        Link = link; Session = session; Info = info;
    }

    public void Dispose() => Link.Dispose();

    public static AdapterConnection Connect(IAdapterPort port, Action<string>? log = null, Action<TimeSpan>? sleep = null,
        int attempts = 3, TimeSpan? bootDelay = null)
    {
        log ??= _ => { };
        sleep ??= Thread.Sleep;
        Exception? last = null;
        for (int a = 1; a <= attempts; a++)
        {
            IAdapterLink? link = null;
            AdapterSession? session = null;
            try
            {
                link = port.Open();
                log($"Porta {link.Name} aperta{(a > 1 ? $" (tentativo {a} di {attempts})" : "")}.");
                session = new AdapterSession(link, log);
                var info = session.Ping();
                log($"Comunicazione OK ({info}).");
                port.ConnectionSucceeded();
                return new AdapterConnection(link, session, info);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                last = ex;
                bool restarted = link?.Lost == true || session?.SawBoot == true;
                link?.Dispose();
                if (!restarted || a == attempts) break; // genuinely silent, or out of attempts
                log("L'adattatore si è riavviato all'apertura della porta: attendo che torni disponibile e riapro.");
                var goneUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < goneUntil && port.IsPresent()) sleep(TimeSpan.FromMilliseconds(100));
                var backUntil = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < backUntil && !port.IsPresent()) sleep(TimeSpan.FromMilliseconds(200));
                sleep(bootDelay ?? TimeSpan.FromSeconds(3));
            }
        }
        throw last is TimeoutException or IOException
            ? new TimeoutException(last.Message, last)
            : last ?? new TimeoutException("Adattatore non raggiungibile.");
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
        bool lostAfterSend = false;
        // 0) open + communication test before sending anything
        AdapterConnection conn;
        try { conn = AdapterConnection.Connect(_port, _log, _sleep, bootDelay: BootDelay); }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new WriteResult(false, false, null, new(), "Profilo NON inviato. " + ex.Message);
        }
        var link = conn.Link;
        var session = conn.Session;
        try
        {
            JsonDocument? reply;
            try
            {
                reply = session.RequestJson(json, ReplyTimeout);
            }
            catch (IOException ex) when (link.Lost || !_port.IsPresent())
            {
                // The firmware answers "ok" and reboots 200 ms later: on Windows the port can vanish
                // while the line is still being sent or before the reply is read.
                _log($"La porta è scomparsa durante l'invio ({ex.Message}).");
                reply = null;
            }
            using (reply)
            {
                if (reply == null)
                {
                    if (!link.Lost && _port.IsPresent())
                        return new WriteResult(false, false, null, new(),
                            "Nessuna risposta dall'adattatore. Controlla che sia collegato solo al PC (non al C64/Amiga), " +
                            "che la Developer Mode sia spenta e che la porta sia quella giusta.");
                    lostAfterSend = true;
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
        }
        finally
        {
            conn.Dispose();
        }
        if (lostAfterSend)
            _log("Probabile riavvio dopo il salvataggio (la risposta non è arrivata in tempo). Verifico rileggendo.");

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
        // Without the "ok" reply the save is only presumed: the read-back is the proof.
        bool confirmed = !lostAfterSend;
        StoredProfile stored;
        try
        {
            _log("Rileggo il profilo…");
            using var readConn = AdapterConnection.Connect(_port, _log, _sleep, bootDelay: BootDelay);
            stored = readConn.Session.ReadStored();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            return new WriteResult(confirmed, false, null, new(),
                (confirmed ? "Profilo salvato" : "Esito incerto") + $", ma la rilettura non è riuscita: {ex.Message}");
        }
        if (stored.Profile == null)
            return new WriteResult(false, false, stored.Method, new(), "Dopo il riavvio l'adattatore non ha un profilo pad salvato: il salvataggio non è riuscito.");

        var diff = ProfileComparer.Compare(profile, stored.Profile);
        string via = stored.Method == ReadMethod.GetJm ? "get_jm" : "dumppad (firmware senza get_jm)";
        if (diff.Count == 0)
            return new WriteResult(true, true, stored.Method, diff, $"Salvato e verificato ({via}): l'adattatore contiene esattamente il profilo inviato.");
        return new WriteResult(confirmed, false, stored.Method, diff, confirmed
            ? $"Salvato, ma il profilo riletto ({via}) è diverso da quello inviato."
            : $"Il profilo sull'adattatore ({via}) è diverso da quello inviato: probabilmente il salvataggio non è avvenuto.");
    }
}
