using UsbToC64.Core.Device;
using UsbToC64.Device;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Progress bar fed by the esptool v4 output (lines as printed when the output is redirected).</summary>
public class ProgressTests
{
    [Theory]
    [InlineData("Connecting....", OpPhase.Connect, null)]
    [InlineData("Chip is ESP32-S3 (QFN56) (revision v0.2)", OpPhase.Connect, null)]
    [InlineData("Erasing flash (this may take a while)...", OpPhase.Erase, null)]
    [InlineData("Writing at 0x00000000... (2 %)", OpPhase.Write, 2)]
    [InlineData("Writing at 0x0003e000... (57 %)", OpPhase.Write, 57)]
    [InlineData("Writing at 0x003fc000... (100 %)", OpPhase.Write, 100)]
    [InlineData("Hash of data verified.", OpPhase.Verify, null)]
    [InlineData("Hard resetting via RTS pin...", OpPhase.Restart, null)]
    public void EsptoolLines(string line, OpPhase phase, int? percent)
    {
        var p = FirmwareUpdater.ParseEsptoolLine(line);
        Assert.NotNull(p);
        Assert.Equal(phase, p!.Phase);
        Assert.Equal(percent, p.Percent);
    }

    [Fact]
    public void WritingMovesTheBarForward_AndEraseHasNoPercentage()
    {
        Assert.Null(FirmwareUpdater.ParseEsptoolLine("Erasing flash (this may take a while)...")!.Overall);
        var a = FirmwareUpdater.ParseEsptoolLine("Writing at 0x00010000... (10 %)")!.Overall!.Value;
        var b = FirmwareUpdater.ParseEsptoolLine("Writing at 0x00200000... (60 %)")!.Overall!.Value;
        Assert.True(a < b && b < 0.88);
        Assert.Null(FirmwareUpdater.ParseEsptoolLine("Wrote 4194304 bytes (1234 compressed) at 0x00000000 in 30.2 seconds"));
    }

    [Fact]
    public void ProfileSave_ReportsItsPhasesInOrder()
    {
        var a = new FakeAdapter();
        var phases = new List<OpPhase>();
        var w = new AdapterWriter(a, _ => { }, _ => { }) { BootDelay = TimeSpan.Zero, Progress = p => phases.Add(p.Phase) };
        var p = new UsbToC64.Core.Model.JmProfile { Vid = 1, Pid = 2 };
        p.Rules.Add(new UsbToC64.Core.Model.JmRule(5, 0x20, UsbToC64.Core.Model.JmOp.BitAny, UsbToC64.Core.Model.JmFunc.Fire1));
        Assert.True(w.WriteAndVerify(p).Verified);
        Assert.Equal(new[] { OpPhase.Connect, OpPhase.Send, OpPhase.Restart, OpPhase.ReadBack, OpPhase.Compare }, phases);
    }
}
