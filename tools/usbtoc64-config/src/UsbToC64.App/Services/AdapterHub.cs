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
    bool NoAnswer = false);

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
                if (!fw.IsOriginal)
                {
                    var stored = conn.Session.ReadStored();
                    pad = stored.Profile;
                    known = true;
                }
                return new AdapterStatus(portName, conn.Info, fw, pad, known, null);
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

    public async Task<WriteResult> WriteAsync(JmProfile profile)
    {
        WriteResult r;
        try
        {
            r = await Run(L.T("hub.op.write"), portName =>
                new AdapterWriter(PortFor(portName), Log).WriteAndVerify(profile));
        }
        catch (Exception ex) { r = new WriteResult(false, false, null, new(), ex.Message); }
        Log(r.Message);
        foreach (var d in r.Differences) Log("  ≠ " + d);
        if (r.Verified) SetStatus(Status with { StoredPad = profile, StoredPadKnown = true });
        return r;
    }

    public async Task<StoredProfile> ReadAsync()
    {
        var s = await Run(L.T("hub.op.read"), portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            return conn.Session.ReadStored();
        });
        SetStatus(Status with { StoredPad = s.Profile, StoredPadKnown = true });
        return s;
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
