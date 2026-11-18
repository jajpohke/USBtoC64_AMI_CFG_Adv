using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Device;

namespace UsbToC64.App.Services;

/// <summary>What the program knows about the adapter (shown in the side card).</summary>
public sealed record AdapterStatus(
    string? Port,
    AdapterInfo? Info,
    AdapterFirmware? Firmware,
    JmProfile? StoredPad,
    bool StoredPadKnown,
    string? Error,
    bool NoAnswer = false,
    IReadOnlyList<SlotContent>? Slots = null)
{
    /// <summary>Firmware 1.3.0+: profile drawers (<see cref="Slots"/> is the list).</summary>
    public bool HasDrawers => Slots != null;

    /// <summary>The profile stored for this pad: its drawer (1.3.0+) or the single stored profile when it is for this pad.</summary>
    public (JmProfile Profile, SlotInfo? Slot)? StoredFor(ushort vid, ushort pid)
    {
        if (Slots != null)
            return Slots.FirstOrDefault(d => d.Info.Vid == vid && d.Info.Pid == pid) is { } d ? (d.Profile, d.Info) : null;
        return StoredPad != null && StoredPad.Vid == vid && StoredPad.Pid == pid ? (StoredPad, null) : null;
    }

    /// <summary>Names of the pads configured on the adapter (labels, or "WebConfigured (VID:PID)").</summary>
    public List<string> StoredNames()
    {
        if (Slots != null)
            return Slots.Select(d => d.Info.IsWebConfigured ? $"{SlotLabels.WebConfigured} ({d.Info.Ids})" : d.Info.Label).ToList();
        return StoredPad != null ? new() { $"{SlotLabels.WebConfigured} ({StoredPad.Vid:X4}:{StoredPad.Pid:X4})" } : new();
    }
}

/// <summary>
/// Everything that talks to the adapter: one operation at a time, a shared log (Registro),
/// the interactive terminal. The terminal is paused while an operation uses the port.
/// </summary>
public sealed class AdapterHub : IDisposable
{
    private readonly SemaphoreSlim _op = new(1, 1);
    private readonly Dictionary<string, SerialAdapterPort> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _logLock = new();
    private readonly List<string> _log = new();

    // terminal
    private IAdapterLink? _termLink;
    private Thread? _termThread;
    private volatile bool _termStop;
    private string? _termPort;

    public List<AdapterPortInfo> Ports { get; private set; } = new();
    /// <summary>null = automatic (the only ESP32 port).</summary>
    public string? RequestedPort { get; set; }
    public FirmwareManifest? Manifest { get; }
    public AdapterStatus Status { get; private set; } = new(null, null, null, null, false, null);
    public bool IsBusy => _op.CurrentCount == 0;
    public bool TerminalConnected => _termLink != null;
    public string? TerminalPort => _termPort;

    /// <summary>A timestamped line for the Registro (and the operation panels).</summary>
    public event Action<string>? LogLine;
    /// <summary>A line received/sent on the terminal connection.</summary>
    public event Action<string>? TerminalLine;
    public event Action? StatusChanged;
    public event Action<bool>? BusyChanged;
    public event Action? TerminalStateChanged;

    public AdapterHub()
    {
        try { Manifest = FirmwareManifest.FindNextTo(AppContext.BaseDirectory); }
        catch (Exception ex) { Log(L.F("hub.manifest.unreadable", ex.Message)); }
    }

    // ------------------------------------------------------------------ log

    public void Log(string line)
    {
        var l = $"{DateTime.Now:HH:mm:ss}  {line}";
        lock (_logLock) _log.Add(l);
        LogLine?.Invoke(l);
    }

    public string LogText()
    {
        lock (_logLock) return string.Join(Environment.NewLine, _log);
    }

    public void ClearLog()
    {
        lock (_logLock) _log.Clear();
    }

    // ------------------------------------------------------------------ ports

    public void RefreshPorts()
    {
        try { Ports = AdapterPorts.List(); }
        catch (Exception) { Ports = new(); }
    }

    private SerialAdapterPort PortFor(string name)
    {
        if (!_ports.TryGetValue(name, out var p)) _ports[name] = p = new SerialAdapterPort(name);
        return p;
    }

    public AdapterPortInfo FindPort() => AdapterPorts.Find(RequestedPort);

    private void SetStatus(AdapterStatus s)
    {
        Status = s;
        StatusChanged?.Invoke();
    }

    // ------------------------------------------------------------------ operations

    /// <summary>Runs one adapter operation in the background, pausing the terminal if it is using the port.</summary>
    private async Task<T> Run<T>(string title, Func<string, T> work)
    {
        await _op.WaitAsync();
        BusyChanged?.Invoke(true);
        string? resumePort = null;
        try
        {
            var port = FindPort();
            Log(L.F("hub.op.start", title, port));
            if (_termLink != null)
            {
                resumePort = _termPort;
                StopTerminal(L.T("hub.term.paused"));
            }
            return await Task.Run(() => work(port.PortName));
        }
        finally
        {
            if (resumePort != null)
            {
                try { StartTerminal(resumePort); }
                catch (Exception ex) { TerminalLine?.Invoke(L.F("hub.term.notReopened", ex.Message)); }
            }
            _op.Release();
            BusyChanged?.Invoke(false);
        }
    }

    /// <summary>Connection test + firmware version + stored pad profile (get_jm only, i.e. firmware 1.2.1+).</summary>
    public async Task<AdapterStatus> CheckAsync()
    {
        string? portName = null;
        try { portName = FindPort().PortName; } catch (Exception) { /* reported below */ }
        try
        {
            var s = await Run(L.T("hub.op.check"), portName =>
            {
                using var conn = AdapterConnection.Connect(PortFor(portName), Log);
                var fw = conn.Session.GetFirmware();
                Log(L.F("hub.check.onAdapter", fw));
                JmProfile? pad = null;
                bool known = false;
                List<SlotContent>? slots = null;
                if (SlotCommands.Supports(fw))
                {
                    slots = conn.Session.ReadAllSlots();
                    Log(L.F("hub.slots.found", slots.Count, FirmwareLimits.MaxSlots));
                    known = true;
                }
                else if (!fw.IsOriginal)
                {
                    var stored = conn.Session.ReadStored();
                    pad = stored.Profile;
                    known = true;
                }
                return new AdapterStatus(portName, conn.Info, fw, pad, known, null, Slots: slots);
            });
            SetStatus(s);
            return s;
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            // An ESP32 port that does not answer: blank, in download mode, or another firmware.
            bool noAnswer = portName != null && ex is TimeoutException;
            var s = new AdapterStatus(portName, null, null, null, false, ex.Message, noAnswer);
            SetStatus(s);
            return s;
        }
    }

    /// <summary>Runs commands on a connected session (connection test included).</summary>
    public Task<T> SessionAsync<T>(string title, Func<AdapterSession, T> work) =>
        Run(title, portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            return work(conn.Session);
        });

    /// <summary>esptool identifies the chip on the port (nothing written).</summary>
    public Task<(bool Found, string Description)> ProbeChipAsync() =>
        Run(L.T("hub.op.probe"), portName =>
        {
            _ports.Remove(portName);
            return new FirmwareUpdater(Log).ProbeChip(portName);
        });

    public async Task<FlashResult> FlashFileAsync(string binPath)
    {
        FlashResult r;
        try
        {
            r = await Run(L.T("hub.op.flashFile"), portName =>
            {
                _ports.Remove(portName);
                return new FirmwareUpdater(Log).UpdateFromFile(portName, binPath);
            });
        }
        catch (Exception ex) { r = new FlashResult(false, ex.Message); }
        Log(r.Message);
        return r;
    }

    /// <param name="slot">Drawer (0-based, firmware 1.3.0+); null = the pad's drawer, else a free one, else the oldest.</param>
    /// <param name="label">Drawer label (firmware 1.3.0+); null = keep the label the pad already has.</param>
    public async Task<WriteResult> WriteAsync(JmProfile profile, int? slot = null, string? label = null)
    {
        WriteResult r;
        try
        {
            r = await Run(L.T("hub.op.write"), portName =>
                new AdapterWriter(PortFor(portName), Log).WriteAndVerify(profile, slot, label));
        }
        catch (Exception ex) { r = new WriteResult(false, false, null, new(), ex.Message); }
        Log(r.Message);
        foreach (var d in r.Differences) Log("  ≠ " + d);
        if (r.Verified)
        {
            if (Status.Slots != null && r.Slot != null)
            {
                // Same rules as the firmware: the drawer is replaced and any other drawer of this pad is emptied.
                uint seq = Status.Slots.Select(d => d.Info.Seq).DefaultIfEmpty(0u).Max() + 1;
                var info = new SlotInfo(r.Slot.Value, profile.Vid, profile.Pid,
                    r.Label == SlotLabels.WebConfigured ? "" : r.Label ?? "", profile.Rules.Count, profile.UseAnalog, seq);
                var list = Status.Slots.Where(d => d.Info.Slot != r.Slot && !(d.Info.Vid == profile.Vid && d.Info.Pid == profile.Pid))
                    .Append(new SlotContent(info, profile)).OrderBy(d => d.Info.Slot).ToList();
                SetStatus(Status with { Slots = list });
            }
            else SetStatus(Status with { StoredPad = profile, StoredPadKnown = true });
        }
        return r;
    }

    // ------------------------------------------------------------------ drawers (firmware 1.3.0)

    private void SetSlots(List<SlotContent> slots) => SetStatus(Status with { Slots = slots });

    /// <summary>Reads all the drawers again.</summary>
    public async Task<List<SlotContent>> RefreshSlotsAsync()
    {
        var list = await Run(L.T("hub.op.slots"), portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            return conn.Session.ReadAllSlots();
        });
        SetSlots(list);
        return list;
    }

    public async Task RenameSlotAsync(int slot, string label)
    {
        var list = await Run(L.F("hub.op.rename", slot + 1), portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            conn.Session.RenameSlot(slot, label);
            return conn.Session.ReadAllSlots();
        });
        Log(L.F("hub.slot.renamed", slot + 1, SlotLabels.Clean(label)));
        SetSlots(list);
    }

    public async Task DeleteSlotAsync(int slot)
    {
        var list = await Run(L.F("hub.op.delete", slot + 1), portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            conn.Session.DeleteSlot(slot);
            return conn.Session.ReadAllSlots();
        });
        Log(L.F("hub.slot.deleted", slot + 1));
        SetSlots(list);
    }

    /// <summary>
    /// Text for the warning shown before a firmware installation, when the adapter holds pad profiles; null otherwise.
    /// </summary>
    public string? StoredPadsWarning()
    {
        var names = Status.StoredNames();
        if (names.Count == 0) return null;
        return L.F("hub.flash.padsWarning", names.Count, string.Join(", ", names));
    }

    /// <summary>
    /// The stored profile: on firmware 1.3.0+ the drawer of this pad (vid/pid), else the single stored profile.
    /// </summary>
    public async Task<(StoredProfile Stored, SlotInfo? Slot)> ReadAsync(ushort? vid = null, ushort? pid = null)
    {
        var (s, slot, slots) = await Run(L.T("hub.op.read"), portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            var fw = conn.Session.GetFirmware();
            if (SlotCommands.Supports(fw))
            {
                var all = conn.Session.ReadAllSlots();
                var d = all.FirstOrDefault(x => x.Info.Vid == vid && x.Info.Pid == pid);
                return (new StoredProfile(d?.Profile, ReadMethod.GetJm, all.Count > 0), d?.Info, all);
            }
            return (conn.Session.ReadStored(), (SlotInfo?)null, (List<SlotContent>?)null);
        });
        SetStatus(slots != null ? Status with { Slots = slots } : Status with { StoredPad = s.Profile, StoredPadKnown = true });
        return (s, slot);
    }

    public async Task<FlashResult> FlashAsync()
    {
        if (Manifest == null) return new FlashResult(false, L.T("hub.noFirmware"));
        var m = Manifest;
        FlashResult r;
        try
        {
            r = await Run(L.T("hub.op.flash"), portName =>
            {
                _ports.Remove(portName); // the board restarts: forget the DTR/RTS combination learnt before
                return new FirmwareUpdater(Log).Update(portName, m);
            });
        }
        catch (Exception ex) { r = new FlashResult(false, ex.Message); }
        Log(r.Message);
        return r;
    }

    // ------------------------------------------------------------------ terminal

    public void ConnectTerminal()
    {
        if (_termLink != null || IsBusy) return;
        StartTerminal(FindPort().PortName);
    }

    public void DisconnectTerminal() => StopTerminal(L.T("hub.term.disconnected"));

    private void StartTerminal(string portName)
    {
        var port = PortFor(portName);
        var link = port.Open();
        _termLink = link;
        _termPort = portName;
        _termStop = false;
        TerminalLine?.Invoke(L.F("hub.term.connected", link.Name));
        _termThread = new Thread(() =>
        {
            var current = link;
            while (!_termStop)
            {
                var line = current.ReadLine(TimeSpan.FromMilliseconds(100));
                if (line != null) { TerminalLine?.Invoke(line); continue; }
                if (!current.Lost) continue;

                // The adapter restarted (reboot, USB reconnection at boot): reconnect like a serial monitor.
                TerminalLine?.Invoke(L.T("hub.term.lost"));
                current.Dispose();
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                IAdapterLink? reopened = null;
                while (!_termStop && reopened == null && DateTime.UtcNow < until)
                {
                    Thread.Sleep(300);
                    if (!port.IsPresent()) continue;
                    Thread.Sleep(1500);
                    try { reopened = port.Open(); }
                    catch (Exception) { /* not ready yet */ }
                }
                if (reopened == null)
                {
                    if (!_termStop) TerminalLine?.Invoke(L.T("hub.term.notBack"));
                    _termLink = null; _termPort = null;
                    TerminalStateChanged?.Invoke();
                    return;
                }
                current = reopened;
                _termLink = reopened;
                TerminalLine?.Invoke(L.F("hub.term.reconnected", reopened.Name));
            }
        })
        { IsBackground = true, Name = "Terminal" };
        _termThread.Start();
        TerminalStateChanged?.Invoke();
    }

    private void StopTerminal(string message)
    {
        if (_termLink == null) return;
        _termStop = true;
        _termThread?.Join(500);
        _termThread = null;
        _termLink.Dispose();
        _termLink = null;
        _termPort = null;
        TerminalLine?.Invoke(message);
        TerminalStateChanged?.Invoke();
    }

    public void SendTerminal(string line)
    {
        if (_termLink == null) throw new InvalidOperationException(L.T("hub.term.notConnected"));
        TerminalLine?.Invoke("> " + line);
        _termLink.SendLine(line);
    }

    public void Dispose()
    {
        StopTerminal("");
        _op.Dispose();
    }
}
