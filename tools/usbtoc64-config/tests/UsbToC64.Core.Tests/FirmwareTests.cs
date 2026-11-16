using System.Security.Cryptography;
using UsbToC64.Core.Device;
using Xunit;

namespace UsbToC64.Core.Tests;

public class FirmwareTests
{
    [Fact]
    public void Version_ParseAndCompare()
    {
        Assert.Equal(new FirmwareVersion(1, 2, 1), FirmwareVersion.Parse("1.2.1"));
        Assert.Equal(new FirmwareVersion(1, 2, 0), FirmwareVersion.Parse("v1.2"));
        Assert.True(FirmwareVersion.Original < FirmwareVersion.Parse("1.2.1"));
        Assert.True(FirmwareVersion.Parse("1.10.0") > FirmwareVersion.Parse("1.9.9"));
    }

    private static FirmwareManifest Manifest(string version, string build = "def5678") => new()
    { Version = FirmwareVersion.Parse(version), Build = build, File = "x.bin", Sha256 = "" };

    [Fact]
    public void Check_OriginalFirmware_NeedsUpdate()
    {
        var original = new AdapterFirmware(FirmwareVersion.Original, null, true);
        Assert.Equal(FirmwareStatus.UpdateAvailable, FirmwareCheck.Compare(original, Manifest("1.2.1")));
    }

    [Fact]
    public void Check_SameVersionDifferentBuild_IsUpdate_SameBuildIsNot()
    {
        var onBoard = new AdapterFirmware(FirmwareVersion.Parse("1.2.1"), "abc1234", false);
        Assert.Equal(FirmwareStatus.OtherBuild, FirmwareCheck.Compare(onBoard, Manifest("1.2.1", "def5678")));
        Assert.Equal(FirmwareStatus.UpToDate, FirmwareCheck.Compare(onBoard, Manifest("1.2.1", "abc1234")));
        Assert.Equal(FirmwareStatus.AdapterNewer, FirmwareCheck.Compare(onBoard, Manifest("1.2.0")));
    }

    [Fact]
    public void Manifest_ParseAndVerifyBin()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bin = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Path.Combine(dir, "USBtoC64_Adv.bin"), bin);
        var sha = Convert.ToHexString(SHA256.HashData(bin)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "firmware.json"),
            $$"""{"version":"1.2.1","build":"abc1234","file":"USBtoC64_Adv.bin","sha256":"{{sha}}","address":"0x0","chip":"esp32s3"}""");
        var m = FirmwareManifest.FindNextTo(dir)!;
        Assert.Equal("1.2.1", m.Version.ToString());
        Assert.Equal(0u, m.Address);
        m.VerifyBin();
        File.WriteAllBytes(m.BinPath, new byte[] { 9 });
        Assert.Throws<InvalidDataException>(m.VerifyBin);
    }

    [Fact]
    public void Session_ReportsOriginalOrVersion()
    {
        using (var c = AdapterConnection.Connect(new FakeAdapter(), null, _ => { }))
            Assert.True(c.Session.GetFirmware().IsOriginal);
        using (var c = AdapterConnection.Connect(new FakeAdapter { Version = "1.2.1" }, null, _ => { }))
        {
            var fw = c.Session.GetFirmware();
            Assert.False(fw.IsOriginal);
            Assert.Equal("1.2.1", fw.Version.ToString());
        }
    }
}
