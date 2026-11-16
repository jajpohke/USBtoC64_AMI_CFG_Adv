using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UsbToC64.Core.Text;

namespace UsbToC64.Core.Device;

/// <summary>Firmware version "major.minor.patch".</summary>
public sealed record FirmwareVersion(int Major, int Minor, int Patch) : IComparable<FirmwareVersion>
{
    /// <summary>The firmware published before versioning: banner "v1.2", no get_version command.</summary>
    public static readonly FirmwareVersion Original = new(1, 2, 0);

    public static FirmwareVersion Parse(string s)
    {
        var parts = s.Trim().TrimStart('v', 'V').Split('.');
        if (parts.Length is < 2 or > 3) throw new FormatException(L.F("fw.badversion", s));
        int P(int i) => i < parts.Length ? int.Parse(parts[i], CultureInfo.InvariantCulture) : 0;
        return new FirmwareVersion(P(0), P(1), P(2));
    }

    public int CompareTo(FirmwareVersion? other) =>
        other is null ? 1 : (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    public static bool operator <(FirmwareVersion a, FirmwareVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(FirmwareVersion a, FirmwareVersion b) => a.CompareTo(b) > 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>What the adapter reports about its firmware.</summary>
public sealed record AdapterFirmware(FirmwareVersion Version, string? Build, bool IsOriginal)
{
    public override string ToString() => IsOriginal
        ? L.T("fw.original")
        : string.IsNullOrEmpty(Build) ? L.F("fw.version", Version) : L.F("fw.versionbuild", Version, Build);
}

/// <summary>firmware.json, written by the GitHub Actions build next to the .bin.</summary>
public sealed class FirmwareManifest
{
    public required FirmwareVersion Version { get; init; }
    public string? Build { get; init; }
    public required string File { get; init; }
    public required string Sha256 { get; init; }
    public uint Address { get; init; }
    public string Chip { get; init; } = "esp32s3";

    /// <summary>Folder that contains the manifest (and the .bin).</summary>
    public string Directory { get; init; } = "";
    public string BinPath => Path.Combine(Directory, File);

    public static FirmwareManifest Parse(string json, string directory)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string S(string name) => r.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        string addr = S("address");
        return new FirmwareManifest
        {
            Version = FirmwareVersion.Parse(S("version")),
            Build = S("build"),
            File = S("file"),
            Sha256 = S("sha256").ToLowerInvariant(),
            Address = addr.Length == 0 ? 0
                : addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? uint.Parse(addr[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : uint.Parse(addr, CultureInfo.InvariantCulture),
            Chip = S("chip") is { Length: > 0 } c ? c : "esp32s3",
            Directory = directory,
        };
    }

    /// <summary>firmware.json next to the program, or null when the program was shipped without firmware.</summary>
    public static FirmwareManifest? FindNextTo(string directory)
    {
        var path = Path.Combine(directory, "firmware.json");
        return System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllText(path), directory) : null;
    }

    /// <summary>Checks that the .bin exists and matches the SHA-256 in the manifest.</summary>
    public void VerifyBin()
    {
        if (!System.IO.File.Exists(BinPath)) throw new FileNotFoundException(L.F("fw.binnotfound", BinPath));
        using var f = System.IO.File.OpenRead(BinPath);
        var hash = Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
        if (hash != Sha256)
            throw new InvalidDataException(L.F("fw.bincorrupt", File));
    }
}

/// <summary>
/// UpdateAvailable: older version number. OtherBuild: same number, different build (development builds): offered
/// in the advanced Firmware page but not required in the simple mode.
/// </summary>
public enum FirmwareStatus { UpdateAvailable, OtherBuild, UpToDate, AdapterNewer }

public static class FirmwareCheck
{
    public static FirmwareStatus Compare(AdapterFirmware onAdapter, FirmwareManifest available)
    {
        if (onAdapter.Version < available.Version) return FirmwareStatus.UpdateAvailable;
        if (onAdapter.Version > available.Version) return FirmwareStatus.AdapterNewer;
        // Same number: a different build of the same version (development builds).
        return !onAdapter.IsOriginal && !string.IsNullOrEmpty(available.Build) && !string.IsNullOrEmpty(onAdapter.Build)
               && onAdapter.Build != "local" && onAdapter.Build != available.Build
            ? FirmwareStatus.OtherBuild
            : FirmwareStatus.UpToDate;
    }
}
