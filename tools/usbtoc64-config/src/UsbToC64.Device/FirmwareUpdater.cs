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

    /// <summary>Arguments for esptool v4: erase-all + write the merged image at the manifest address.</summary>
    public static string BuildArguments(string port, FirmwareManifest m) =>
        $"--chip {m.Chip} --port {port} --baud 921600 --before default_reset --after hard_reset " +
        $"write_flash --erase-all 0x{m.Address:X} \"{m.BinPath}\"";

    public FlashResult Update(string portName, FirmwareManifest manifest)
    {
        try { manifest.VerifyBin(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return new(false, ex.Message); }

        var esptool = FindEsptool(manifest.Directory) ?? FindEsptool(AppContext.BaseDirectory);
        if (esptool == null)
            return new(false, "esptool.exe non trovato accanto al programma: lo zip della build deve contenerlo.");

        var args = BuildArguments(portName, manifest);
        _log($"Aggiornamento a {manifest.Version} (build {manifest.Build}) su {portName}");
        _log($"> esptool {args}");

        int exit;
        try { exit = Run(esptool, args, TimeSpan.FromMinutes(5)); }
        catch (Exception ex) { return new(false, $"esptool non si è avviato: {ex.Message}"); }
        if (exit != 0)
            return new(false, $"esptool è terminato con errore ({exit}). L'adattatore potrebbe essere senza firmware: " +
                              "riprova; se non risponde, tieni premuto BOOT mentre colleghi la USB-C e ripeti l'aggiornamento.");

        // esptool resets the chip at the end (hard_reset): wait for the port and the boot, then check the version.
        _log("Scrittura completata. Attendo il riavvio dell'adattatore…");
        var port = new SerialAdapterPort(portName);
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < until && !port.IsPresent()) Thread.Sleep(200);
        Thread.Sleep(3500); // firmware start-up delay (2 s) + banner

        try
        {
            using var conn = AdapterConnection.Connect(port, _log);
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

    private static readonly Regex Progress = new(@"\((\d+) ?%\)");

    private int Run(string exe, string args, TimeSpan timeout)
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
