using System.IO.Ports;
using System.Text;
using Microsoft.Win32;
using UsbToC64.Core.Device;
using UsbToC64.Core.Model;

namespace UsbToC64.Device;

public sealed record AdapterPortInfo(string PortName, string Description, bool IsEspressif)
{
    public override string ToString() => IsEspressif ? $"{PortName} — {Description}" : $"{PortName} — {Description} (non ESP32)";
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
            result.Add(new AdapterPortInfo(p, "porta seriale", false));

        return result.OrderByDescending(p => p.IsEspressif).ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The adapter port: the only ESP32 port, or the explicitly requested one.</summary>
    public static AdapterPortInfo Find(string? requested = null)
    {
        var all = List();
        if (!string.IsNullOrWhiteSpace(requested))
            return all.FirstOrDefault(p => string.Equals(p.PortName, requested, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"La porta {requested} non è presente.");
        var esp = all.Where(p => p.IsEspressif).ToList();
        return esp.Count switch
        {
            1 => esp[0],
            0 => throw new InvalidOperationException(
                "Adattatore non trovato: nessuna porta ESP32 (VID 303A). Collegalo al PC con la USB-C, senza il C64/Amiga, " +
                "oppure indica la porta a mano."),
            _ => throw new InvalidOperationException(
                $"Trovati {esp.Count} ESP32 ({string.Join(", ", esp.Select(p => p.PortName))}): indica la porta a mano."),
        };
    }
}

/// <summary>A COM port as an <see cref="IAdapterPort"/>.</summary>
public sealed class SerialAdapterPort : IAdapterPort
{
    public SerialAdapterPort(string portName) => Name = portName;
    public string Name { get; }

    public bool IsPresent() => SerialPort.GetPortNames().Contains(Name, StringComparer.OrdinalIgnoreCase);

    public IAdapterLink Open()
    {
        // Same signals as the web configurator (Web Serial asserts DTR and RTS on open).
        var sp = new SerialPort(Name, 115200, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.UTF8,
            NewLine = "\n",
            ReadTimeout = 50,
            WriteTimeout = 2000,
        };
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { sp.Open(); return new SerialLink(sp); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                last = ex;
                Thread.Sleep(300);
            }
        }
        sp.Dispose();
        throw new IOException(last is UnauthorizedAccessException
            ? $"La porta {Name} è occupata da un altro programma (terminale seriale, browser con il configuratore web…)."
            : $"Impossibile aprire {Name}: {last?.Message}", last);
    }
}

internal sealed class SerialLink : IAdapterLink
{
    private readonly SerialPort _sp;
    private readonly StringBuilder _buf = new();

    public SerialLink(SerialPort sp) => _sp = sp;
    public string Name => _sp.PortName;
    public bool Lost { get; private set; }

    public void SendLine(string line)
    {
        _sp.Write(line + "\n");
        _sp.BaseStream.Flush();
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
