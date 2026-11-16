using System.Diagnostics;
using System.Text.RegularExpressions;
using UsbToC64.Core.Device;

namespace UsbToC64.Device;

public sealed record FlashResult(bool Success, string Message);

/// <summary>
/// Firmware update with esptool (shipped next to the program): erase the whole flash, write the full image,
/// then check the version the adapter reports. All settings go back to defaults, like "erase all" on the web page.
/// </summary>
public sealed class FirmwareUpdater
{
    private readonly Action<string> _log;

    public FirmwareUpdater(Action<string>? log = null) => _log = log ?? (_ => { });

    public static string? FindEsptool(string directory)
    {
        foreach (var name in new[] { "esptool.exe", "esptool" })
        {
            var p = Path.Combine(directory, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Arguments for esptool v4: erase-all + write a full image.</summary>
    public static string BuildArguments(string port, string chip, uint address, string binPath) =>
        $"--chip {chip} --port {port} --baud 921600 --before default_reset --after hard_reset " +
        $"write_flash --erase-all 0x{address:X} \"{binPath}\"";

    public static string BuildArguments(string port, FirmwareManifest m) => BuildArguments(port, m.Chip, m.Address, m.BinPath);

    /// <summary>The firmware included with the program (firmware.json): SHA-256 check, then the version must match.</summary>
    public FlashResult Update(string portName, FirmwareManifest manifest)
    {
        try { manifest.VerifyBin(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return new(false, ex.Message); }
        _log($"Aggiornamento a {manifest.Version} (build {manifest.Build}) su {portName}");
        var flashed = Flash(portName, manifest.Chip, manifest.Address, manifest.BinPath, manifest.Directory);
        if (flashed != null) return flashed;

        try
        {
            using var conn = ConnectAfterFlash(portName);
            var fw = conn.Session.GetFirmware();
            _log($"Sull'adattatore ora c'è: {fw}");
            bool ok = !fw.IsOriginal && fw.Version.CompareTo(manifest.Version) == 0
                      && (string.IsNullOrEmpty(manifest.Build) || fw.Build == manifest.Build);
            return ok
                ? new(true, $"Firmware aggiornato a {manifest.Version}. Tutte le impostazioni sono tornate ai valori predefiniti: " +
                            "scrivi di nuovo il profilo del pad.")
                : new(false, $"Il flash è terminato, ma l'adattatore riporta {fw} invece di {manifest.Version} (build {manifest.Build}).");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, $"Il flash è terminato, ma l'adattatore non risponde dopo il riavvio: {ex.Message}");
        }
    }

    /// <summary>
    /// A full image chosen by the user (e.g. Emanuele's firmware), always at 0x0. A different firmware may not
    /// know this program's commands: a missing answer after the flash is reported, not treated as a failure.
    /// </summary>
    public FlashResult UpdateFromFile(string portName, string binPath, string chip = "esp32s3")
    {
        if (!File.Exists(binPath)) return new(false, $"File non trovato: {binPath}");
        var size = new FileInfo(binPath).Length;
        if (size < 0x10000 || size > 16 * 1024 * 1024)
            return new(false, $"Il file {Path.GetFileName(binPath)} ({size} byte) non sembra un'immagine completa del firmware.");
        _log($"Installazione di {Path.GetFileName(binPath)} ({size} byte) su {portName}, indirizzo 0x0");
        var flashed = Flash(portName, chip, 0, binPath, Path.GetDirectoryName(binPath) ?? "");
        if (flashed != null) return flashed;

        try
        {
            using var conn = ConnectAfterFlash(portName);
            var fw = conn.Session.GetFirmware();
            _log($"Sull'adattatore ora c'è: {fw}");
            return new(true, $"Firmware {Path.GetFileName(binPath)} installato. L'adattatore risponde ({fw}). " +
                             "Tutte le impostazioni sono tornate ai valori predefiniti.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(true, $"Firmware {Path.GetFileName(binPath)} scritto. L'adattatore non risponde ai comandi di questo programma: " +
                             $"è normale con un firmware diverso ({ex.Message}).");
        }
    }

    /// <summary>
    /// Asks esptool to identify the chip without writing anything (the board is reset back to its program).
    /// Used when an ESP32 port is present but does not answer the program's commands.
    /// </summary>
    public (bool Found, string Description) ProbeChip(string portName)
    {
        var esptool = FindEsptool(AppContext.BaseDirectory);
        if (esptool == null) return (false, "esptool.exe non trovato accanto al programma");
        var lines = new List<string>();
        int exit;
        try { exit = Run(esptool, $"--port {portName} --before default_reset --after hard_reset chip_id", TimeSpan.FromSeconds(30), lines.Add, quiet: true); }
        catch (Exception ex) { return (false, ex.Message); }
        var chip = lines.FirstOrDefault(l => l.Contains("Chip is", StringComparison.OrdinalIgnoreCase))
                   ?? lines.FirstOrDefault(l => l.Contains("Detecting chip type", StringComparison.OrdinalIgnoreCase));
        foreach (var l in lines) _log("  " + l);
        return exit == 0 ? (true, chip?.Trim() ?? "ESP32") : (false, lines.LastOrDefault() ?? $"esptool terminato con {exit}");
    }

    /// <returns>null when esptool succeeded, otherwise the failure.</returns>
    private FlashResult? Flash(string portName, string chip, uint address, string binPath, string directory)
    {
        var esptool = FindEsptool(directory) ?? FindEsptool(AppContext.BaseDirectory);
        if (esptool == null)
            return new(false, "esptool.exe non trovato accanto al programma: lo zip della build deve contenerlo.");
        var args = BuildArguments(portName, chip, address, binPath);
        _log($"> esptool {args}");
        int exit;
        try { exit = Run(esptool, args, TimeSpan.FromMinutes(5), null, quiet: false); }
        catch (Exception ex) { return new(false, $"esptool non si è avviato: {ex.Message}"); }
        if (exit != 0)
            return new(false, $"esptool è terminato con errore ({exit}). L'adattatore potrebbe essere senza firmware: " +
                              "riprova; se non risponde, tieni premuto BOOT mentre colleghi la USB-C e ripeti l'installazione.");
        _log("Scrittura completata. Attendo il riavvio dell'adattatore…");
        return null;
    }

    /// <summary>esptool resets the chip at the end (hard_reset): wait for the port and the boot, then connect.</summary>
    private AdapterConnection ConnectAfterFlash(string portName)
    {
        var port = new SerialAdapterPort(portName);
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < until && !port.IsPresent()) Thread.Sleep(200);
        Thread.Sleep(3500); // firmware start-up delay (2 s) + banner
        return AdapterConnection.Connect(port, _log);
    }

    private static readonly Regex Progress = new(@"\((\d+) ?%\)");

    private int Run(string exe, string args, TimeSpan timeout, Action<string>? capture, bool quiet)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };
        using var p = new Process { StartInfo = psi };
        int lastPct = -10;
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            capture?.Invoke(line.Trim());
            if (quiet) return;
            // esptool prints a progress line per block: keep one every 10 %.
            var m = Progress.Match(line);
            if (m.Success && line.Contains("Writing at", StringComparison.Ordinal))
            {
                int pct = int.Parse(m.Groups[1].Value);
                if (pct < 100 && pct - lastPct < 10) return;
                lastPct = pct;
            }
            _log("  " + line.Trim());
        }
        p.OutputDataReceived += (_, e) => OnLine(e.Data);
        p.ErrorDataReceived += (_, e) => OnLine(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            _log("esptool non ha terminato entro il tempo massimo: interrotto.");
            return -1;
        }
        p.WaitForExit(); // flush the async output
        return p.ExitCode;
    }
}
