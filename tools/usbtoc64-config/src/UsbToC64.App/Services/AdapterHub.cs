using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Device;

namespace UsbToC64.App.Services;

/// <summary>What the program knows about the adapter (shown in the side card).</summary>
public sealed record AdapterStatus(
    string? Port,
    AdapterInfo? Info,
    AdapterFirmware? Firmware,
    JmProfile? StoredPad,
    bool StoredPadKnown,
    string? Error);

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
        catch (Exception ex) { Log($"firmware.json non leggibile: {ex.Message}"); }
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
            Log($"--- {title} su {port}");
            if (_termLink != null)
            {
                resumePort = _termPort;
                StopTerminal("(terminale in pausa: operazione in corso)");
            }
            return await Task.Run(() => work(port.PortName));
        }
        finally
        {
            if (resumePort != null)
            {
                try { StartTerminal(resumePort); }
                catch (Exception ex) { TerminalLine?.Invoke($"(terminale non riaperto: {ex.Message})"); }
            }
            _op.Release();
            BusyChanged?.Invoke(false);
        }
    }

    /// <summary>Connection test + firmware version + stored pad profile (get_jm only, i.e. firmware 1.2.1+).</summary>
    public async Task<AdapterStatus> CheckAsync()
    {
        try
        {
            var s = await Run("Controllo adattatore", portName =>
            {
                using var conn = AdapterConnection.Connect(PortFor(portName), Log);
                var fw = conn.Session.GetFirmware();
                Log($"Sull'adattatore: {fw}");
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
            var s = new AdapterStatus(null, null, null, null, false, ex.Message);
            SetStatus(s);
            return s;
        }
    }

    public async Task<WriteResult> WriteAsync(JmProfile profile)
    {
        WriteResult r;
        try
        {
            r = await Run("Scrittura del profilo", portName =>
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
        var s = await Run("Lettura del profilo", portName =>
        {
            using var conn = AdapterConnection.Connect(PortFor(portName), Log);
            return conn.Session.ReadStored();
        });
        SetStatus(Status with { StoredPad = s.Profile, StoredPadKnown = true });
        return s;
    }

    public async Task<FlashResult> FlashAsync()
    {
        if (Manifest == null) return new FlashResult(false, "Questo programma non include un firmware (manca firmware.json).");
        var m = Manifest;
        FlashResult r;
        try
        {
            r = await Run("Aggiornamento firmware", portName =>
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

    public void DisconnectTerminal() => StopTerminal("(terminale scollegato)");

    private void StartTerminal(string portName)
    {
        var port = PortFor(portName);
        var link = port.Open();
        _termLink = link;
        _termPort = portName;
        _termStop = false;
        TerminalLine?.Invoke($"(collegato a {link.Name})");
        _termThread = new Thread(() =>
        {
            var current = link;
            while (!_termStop)
            {
                var line = current.ReadLine(TimeSpan.FromMilliseconds(100));
                if (line != null) { TerminalLine?.Invoke(line); continue; }
                if (!current.Lost) continue;

                // The adapter restarted (reboot, USB reconnection at boot): reconnect like a serial monitor.
                TerminalLine?.Invoke("(porta persa: attendo che l'adattatore torni disponibile…)");
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
                    if (!_termStop) TerminalLine?.Invoke("(adattatore non tornato: terminale scollegato)");
                    _termLink = null; _termPort = null;
                    TerminalStateChanged?.Invoke();
                    return;
                }
                current = reopened;
                _termLink = reopened;
                TerminalLine?.Invoke($"(ricollegato a {reopened.Name})");
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
        if (_termLink == null) throw new InvalidOperationException("Terminale non collegato.");
        TerminalLine?.Invoke("> " + line);
        _termLink.SendLine(line);
    }

    public void Dispose()
    {
        StopTerminal("");
        _op.Dispose();
    }
}
