using System.IO.Ports;
using System.Text;
using Microsoft.Win32;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;

namespace UsbToC64.Device;

public sealed record AdapterPortInfo(string PortName, string Description, bool IsEspressif)
{
    public override string ToString() => IsEspressif ? $"{PortName} — {Description}" : L.F("serial.portnotesp", PortName, Description);
}

public static class AdapterPorts
{
    /// <summary>
    /// Serial ports currently present. On Windows the ESP32-S3 native USB port is recognised by its
    /// vendor id (0x303A, Espressif) in the registry, so the right port is found without asking.
    /// </summary>
    public static List<AdapterPortInfo> List()
    {
        var present = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<AdapterPortInfo>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (usb != null)
                {
                    foreach (var dev in usb.GetSubKeyNames())
                    {
                        bool esp = dev.StartsWith($"VID_{FirmwareLimits.EspressifVid:X4}", StringComparison.OrdinalIgnoreCase);
                        using var devKey = usb.OpenSubKey(dev);
                        if (devKey == null) continue;
                        foreach (var inst in devKey.GetSubKeyNames())
                        {
                            using var instKey = devKey.OpenSubKey(inst);
                            using var parms = instKey?.OpenSubKey("Device Parameters");
                            if (parms?.GetValue("PortName") is not string port || !present.Contains(port) || known.Contains(port)) continue;
                            string desc = instKey?.GetValue("FriendlyName") as string ?? instKey?.GetValue("DeviceDesc") as string ?? dev;
                            int semi = desc.LastIndexOf(';');
                            if (semi >= 0) desc = desc[(semi + 1)..]; // "@usbser.inf,%...%;USB Serial Device"
                            result.Add(new AdapterPortInfo(port, desc.Trim(), esp));
                            known.Add(port);
                        }
                    }
                }
            }
            catch (Exception) { /* registry not readable: fall back to the plain list */ }
        }

        foreach (var p in present.Where(p => !known.Contains(p)))
            result.Add(new AdapterPortInfo(p, L.T("serial.port"), false));

        return result.OrderByDescending(p => p.IsEspressif).ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The adapter port: the only ESP32 port, or the explicitly requested one.</summary>
    public static AdapterPortInfo Find(string? requested = null)
    {
        var all = List();
        if (!string.IsNullOrWhiteSpace(requested))
            return all.FirstOrDefault(p => string.Equals(p.PortName, requested, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException(L.F("serial.notpresent", requested));
        var esp = all.Where(p => p.IsEspressif).ToList();
        return esp.Count switch
        {
            1 => esp[0],
            0 => throw new InvalidOperationException(
                L.T("serial.notfound")),
            _ => throw new InvalidOperationException(
                L.F("serial.multiple", esp.Count, string.Join(", ", esp.Select(p => p.PortName)))),
        };
    }
}

/// <summary>A COM port as an <see cref="IAdapterPort"/>.</summary>
public sealed class SerialAdapterPort : IAdapterPort
{
    public SerialAdapterPort(string portName) => Name = portName;
    public string Name { get; }

    public bool IsPresent() => SerialPort.GetPortNames().Contains(Name, StringComparer.OrdinalIgnoreCase);

    private int _opens;

    /// <summary>
    /// DTR/RTS used for each open. ESP32-S3 native USB (USB-Serial-JTAG) resets the chip when RTS is active
    /// while DTR is not ("rst:0x15 (USB_UART_CHIP_RESET)"); opening the port can trigger it depending on how
    /// Windows applies the two lines. Like the Arduino IDE serial monitor, after a restart the port is reopened
    /// with both lines active; the combinations alternate so a reopen never repeats the one that just reset it.
    /// </summary>
    private static readonly (bool Dtr, bool Rts)[] LineModes = { (true, true), (true, false), (false, false) };

    public string LastLines { get; private set; } = "";

    private int? _goodMode;
    private int _lastMode;

    public void ConnectionSucceeded() => _goodMode = _lastMode;

    public IAdapterLink Open()
    {
        _lastMode = _goodMode ?? _opens++ % LineModes.Length;
        var (dtr, rts) = LineModes[_lastMode];
        LastLines = $"DTR={(dtr ? 1 : 0)} RTS={(rts ? 1 : 0)}";

        // Windows: open like the Arduino IDE (one SetCommState, no intermediate DTR/RTS changes).
        if (OperatingSystem.IsWindows())
        {
            Exception? err = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { return Win32SerialLink.Open(Name, dtr, rts); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    err = ex;
                    Thread.Sleep(300);
                }
            }
            throw err is UnauthorizedAccessException ua ? new IOException(ua.Message, ua) : (IOException)err!;
        }

        var sp = new SerialPort(Name, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = dtr,
            RtsEnable = rts,
            Encoding = Encoding.UTF8,
            NewLine = "\n",
            ReadTimeout = 50,
            WriteTimeout = 2000,
        };
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { sp.Open(); return new SerialLink(sp, LastLines); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                last = ex;
                Thread.Sleep(300);
            }
        }
        sp.Dispose();
        throw new IOException(last is UnauthorizedAccessException
            ? L.F("serial.busy", Name)
            : L.F("serial.cannotopen", Name, last?.Message), last);
    }
}

internal sealed class SerialLink : IAdapterLink
{
    private readonly SerialPort _sp;
    private readonly StringBuilder _buf = new();

    private readonly string _lines;
    public SerialLink(SerialPort sp, string lines) { _sp = sp; _lines = lines; }
    public string Name => $"{_sp.PortName} ({_lines})";
    public bool Lost { get; private set; }

    private const int ChunkSize = 32;
    private const int ChunkDelayMs = 15;

    public void SendLine(string line)
    {
        // Sent in small paced chunks: the original firmware has a 256-byte USB RX buffer and reads it only
        // from loop(), so a ~700-byte save_jm line written at once overflows it and is silently dropped.
        // No BaseStream.Flush(): on Windows it can block until the adapter reboots and then fail.
        var data = line + "\n";
        try
        {
            for (int i = 0; i < data.Length; i += ChunkSize)
            {
                _sp.Write(data.Substring(i, Math.Min(ChunkSize, data.Length - i)));
                if (i + ChunkSize < data.Length) Thread.Sleep(ChunkDelayMs);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Lost = true;
            throw new IOException(L.F("serial.sendfailed", Name, ex.Message), ex);
        }
    }

    public string? ReadLine(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var s = _buf.ToString();
            int nl = s.IndexOf('\n');
            if (nl >= 0)
            {
                _buf.Remove(0, nl + 1);
                return s[..nl].TrimEnd('\r');
            }
            if (DateTime.UtcNow >= until) return null;
            try
            {
                string chunk = _sp.ReadExisting();
                if (chunk.Length > 0) _buf.Append(chunk);
                else Thread.Sleep(10);
            }
            catch (TimeoutException) { }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // The device went away (reboot, unplug): Windows reports "device not functioning".
                Lost = true;
                return null;
            }
        }
    }

    public void DiscardInput()
    {
        try { _sp.DiscardInBuffer(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { Lost = true; }
        _buf.Clear();
    }

    public void Dispose()
    {
        // Closing a port whose device has disappeared throws on Windows: ignore, it is gone anyway.
        try { if (_sp.IsOpen) _sp.Close(); } catch (Exception) { }
        try { _sp.Dispose(); } catch (Exception) { }
    }
}
