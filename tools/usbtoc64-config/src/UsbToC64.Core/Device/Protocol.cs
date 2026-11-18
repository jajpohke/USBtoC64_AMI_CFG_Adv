using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;

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
            d.Add(L.F("proto.diff.vidpid", sent.Vid, sent.Pid, stored.Vid, stored.Pid));
        if (sent.DpadIndex != stored.DpadIndex)
            d.Add(L.F("proto.diff.dpad", sent.DpadIndex, stored.DpadIndex));

        var expectedRules = sent.Rules.Take(FirmwareLimits.MaxRules).ToList();
        if (sent.Rules.Count > FirmwareLimits.MaxRules)
            d.Add(L.F("proto.diff.maxrules", FirmwareLimits.MaxRules, sent.Rules.Count));
        if (expectedRules.Count != stored.Rules.Count)
            d.Add(L.F("proto.diff.count", expectedRules.Count, stored.Rules.Count));
        for (int i = 0; i < Math.Min(expectedRules.Count, stored.Rules.Count); i++)
            if (expectedRules[i] != stored.Rules[i])
                d.Add(L.F("proto.diff.rule", i + 1, Names.Rule(expectedRules[i]), Names.Rule(stored.Rules[i])));

        static string Arr(IEnumerable<byte> b) => "[" + string.Join(", ", b) + "]";
        var ex = sent.AnalogX.Take(FirmwareLimits.MaxAnalogPerAxis).ToList();
        var ey = sent.AnalogY.Take(FirmwareLimits.MaxAnalogPerAxis).ToList();
        if (!ex.SequenceEqual(stored.AnalogX)) d.Add(L.F("proto.diff.axesx", Arr(ex), Arr(stored.AnalogX)));
        if (!ey.SequenceEqual(stored.AnalogY)) d.Add(L.F("proto.diff.axesy", Arr(ey), Arr(stored.AnalogY)));
        return d;
    }
}

/// <summary>Parses the text printed by the service-menu command <c>dumppad</c> (TIER 0 section).</summary>
public static class DumppadParser
{
    private static readonly Dictionary<string, JmFunc> FuncByName =
        Enum.GetValues<JmFunc>().ToDictionary(f => Names.Func(f), f => f);

    private static readonly Dictionary<string, JmOp> OpByName = new()
    {
        ["=="] = JmOp.Eq, ["BIT"] = JmOp.BitAny, ["HAT"] = JmOp.Hat, ["S<"] = JmOp.SignedLess, ["S>"] = JmOp.SignedGreater,
    };

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
        foreach (Match r in Regex.Matches(t, @"Rule\s+\d+\s*:\s*Byte\[(\d+)\]\s*(==|BIT|HAT|S<|S>|\?\?\?)\s*(\d+)\s*->\s*Action:\s*([^\r\n]+)"))
        {
            string opName = r.Groups[2].Value, funcName = r.Groups[4].Value.Trim();
            if (!OpByName.TryGetValue(opName, out var op) || !FuncByName.TryGetValue(funcName, out var f))
                throw new FormatException(L.F("proto.dumppad.badrule", r.Value.Trim()));
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
public sealed record AdapterInfo(bool IsAmiga, bool DevMode, bool InvertSwitch, string Raw, bool DevAmiga = false)
{
    public override string ToString() => L.F("proto.info", IsAmiga ? "Amiga" : "C64", DevMode ? "ON" : "OFF");
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
                _log(L.T("proto.rebooting"));
                WaitForBanner(TimeSpan.FromSeconds(8));
            }
            if (_link.Lost)
                throw new IOException(L.T("proto.portclosed"));
            Thread.Sleep(300);
            doc = RequestJson("{\"cmd\":\"get_info\"}", timeout ?? TimeSpan.FromSeconds(2));
        }
        using var reply = doc
            ?? throw new TimeoutException(L.T("proto.noping"));
        var r = reply.RootElement;
        bool B(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        if (!r.TryGetProperty("is_amiga", out _))
            throw new InvalidOperationException(L.F("proto.badping", r.GetRawText()));
        return new AdapterInfo(B("is_amiga"), B("dev_mode"), B("inv_switch"), r.GetRawText(), B("dev_amiga"));
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

    /// <summary>Firmware version (get_version). The original firmware does not know the command and stays silent.</summary>
    public AdapterFirmware GetFirmware()
    {
        using var doc = RequestJson("{\"cmd\":\"get_version\"}", TimeSpan.FromMilliseconds(1500));
        if (doc == null || !doc.RootElement.TryGetProperty("fw", out var fw) || fw.GetString() is not { } v)
            return new AdapterFirmware(FirmwareVersion.Original, null, IsOriginal: true);
        string? build = doc.RootElement.TryGetProperty("build", out var b) ? b.GetString() : null;
        return new AdapterFirmware(FirmwareVersion.Parse(v), build, IsOriginal: false);
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
                    throw new InvalidOperationException(L.F("proto.replyerror", root.GetRawText()));
                bool hasCustom = root.TryGetProperty("has_custom", out var hc) && hc.ValueKind == JsonValueKind.True;
                // Same format as save_jm, without "cmd".
                return new StoredProfile(SaveJmJson.Parse(root.GetRawText()), ReadMethod.GetJm, hasCustom);
            }
        }
        _log(L.T("proto.nogetjm"));
        var text = RequestText("dumppad", TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(4));
        if (text.Length == 0) throw new TimeoutException(L.T("proto.noreplyboth"));
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
                log(a > 1 ? L.F("proto.portopenattempt", link.Name, a, attempts) : L.F("proto.portopen", link.Name));
                session = new AdapterSession(link, log);
                var info = session.Ping();
                log(L.F("proto.commok", info));
                port.ConnectionSucceeded();
                return new AdapterConnection(link, session, info);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                last = ex;
                bool restarted = link?.Lost == true || session?.SawBoot == true;
                link?.Dispose();
                if (!restarted || a == attempts) break; // genuinely silent, or out of attempts
                log(L.T("proto.restartedonopen"));
                var goneUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < goneUntil && port.IsPresent()) sleep(TimeSpan.FromMilliseconds(100));
                var backUntil = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < backUntil && !port.IsPresent()) sleep(TimeSpan.FromMilliseconds(200));
                sleep(bootDelay ?? TimeSpan.FromSeconds(3));
            }
        }
        throw last is TimeoutException or IOException
            ? new TimeoutException(last.Message, last)
            : last ?? new TimeoutException(L.T("proto.unreachable"));
    }
}

/// <param name="Slot">Drawer written (firmware 1.3.0+), 0-based.</param>
/// <param name="Label">Label stored in the drawer, as read back.</param>
public sealed record WriteResult(bool Saved, bool Verified, ReadMethod? Method, List<string> Differences, string Message,
    int? Slot = null, string? Label = null);

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

    /// <param name="slot">Drawer (0-based) on firmware 1.3.0+; null = the pad's own drawer, else a free one, else the oldest.</param>
    /// <param name="label">Drawer label on firmware 1.3.0+; null = keep the label of the pad's drawer.</param>
    public WriteResult WriteAndVerify(JmProfile profile, int? slot = null, string? label = null)
    {
        if (label != null) label = SlotLabels.Clean(label);
        int? savedSlot = null;

        // 1) send and wait for the reply
        bool lostAfterSend = false;
        // 0) open + communication test before sending anything
        AdapterConnection conn;
        try { conn = AdapterConnection.Connect(_port, _log, _sleep, bootDelay: BootDelay); }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new WriteResult(false, false, null, new(), L.F("proto.notsent", ex.Message));
        }
        var link = conn.Link;
        var session = conn.Session;

        var fw = session.GetFirmware();
        bool drawers = SlotCommands.Supports(fw);
        string json = drawers ? SaveJmJson.Serialize(profile, slot, label) : SaveJmJson.Serialize(profile);
        // Signed rules need a firmware that knows them: an older one would treat them as "==".
        if (profile.Rules.Any(r => FirmwareLimits.IsSigned(r.Op)))
        {
            if (fw.IsOriginal || fw.Version < FirmwareVersion.Parse(FirmwareLimits.SignedOpsMinFirmware))
            {
                conn.Dispose();
                return new WriteResult(false, false, null, new(),
                    L.F("proto.notsentsigned", FirmwareLimits.SignedOpsMinFirmware, fw));
            }
        }
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
                _log(L.F("proto.portgone", ex.Message));
                reply = null;
            }
            using (reply)
            {
                if (reply == null)
                {
                    if (!link.Lost && _port.IsPresent())
                        return new WriteResult(false, false, null, new(),
                            L.T("proto.noreply"));
                    lostAfterSend = true;
                }
                else
                {
                    var root = reply.RootElement;
                    string status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                    if (status != "ok")
                        return new WriteResult(false, false, null, new(), L.F("proto.rejected", root.GetRawText()));
                    if (root.TryGetProperty("slot", out var sl) && sl.ValueKind == JsonValueKind.Number) savedSlot = sl.GetInt32();
                    _log(savedSlot != null ? L.F("slot.accepted", savedSlot + 1) : L.T("proto.accepted"));
                }
            }
        }
        finally
        {
            conn.Dispose();
        }
        if (lostAfterSend)
            _log(L.T("proto.probablereboot"));

        // 2) wait for the reboot: the port disappears and comes back
        var deadline = DateTime.UtcNow + RebootTimeout;
        var goneUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < goneUntil && _port.IsPresent()) _sleep(TimeSpan.FromMilliseconds(100));
        while (DateTime.UtcNow < deadline && !_port.IsPresent()) _sleep(TimeSpan.FromMilliseconds(200));
        if (!_port.IsPresent())
            return new WriteResult(true, false, null, new(),
                L.T("proto.noportafter"));
        _sleep(BootDelay);

        // 3) read back and compare
        // Without the "ok" reply the save is only presumed: the read-back is the proof.
        bool confirmed = !lostAfterSend;
        StoredProfile stored;
        SlotContent? drawer = null;
        try
        {
            _log(L.T("proto.readingback"));
            using var readConn = AdapterConnection.Connect(_port, _log, _sleep, bootDelay: BootDelay);
            if (drawers)
            {
                // The drawer named in the reply; without the reply, the drawer that now holds this pad.
                int? which = savedSlot ?? slot
                             ?? readConn.Session.ListSlots().FirstOrDefault(s => s.Vid == profile.Vid && s.Pid == profile.Pid)?.Slot;
                drawer = which == null ? null : readConn.Session.ReadSlot(which.Value);
                stored = new StoredProfile(drawer?.Profile, ReadMethod.GetJm, drawer != null);
            }
            else stored = readConn.Session.ReadStored();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            return new WriteResult(confirmed, false, null, new(),
                confirmed ? L.F("proto.readbackfailed.saved", ex.Message) : L.F("proto.readbackfailed.uncertain", ex.Message));
        }
        if (stored.Profile == null)
            return new WriteResult(false, false, stored.Method, new(), L.T("proto.noprofileafter"));

        var diff = ProfileComparer.Compare(profile, stored.Profile);
        if (drawer != null && label != null && drawer.Info.Label != label)
            diff.Add(L.F("slot.labeldiff", label, drawer.Info.DisplayLabel));
        string via = stored.Method == ReadMethod.GetJm ? "get_jm" : L.T("proto.via.dumppad");
        int? where = drawer?.Info.Slot;
        string? stLabel = drawer?.Info.DisplayLabel;
        if (diff.Count == 0)
            return new WriteResult(true, true, stored.Method, diff,
                drawer != null ? L.F("slot.verified", via, drawer.Info.Number, drawer.Info.DisplayLabel) : L.F("proto.verified", via),
                where, stLabel);
        return new WriteResult(confirmed, false, stored.Method, diff, confirmed
            ? L.F("proto.differs.saved", via)
            : L.F("proto.differs.notsaved", via), where, stLabel);
    }
}
