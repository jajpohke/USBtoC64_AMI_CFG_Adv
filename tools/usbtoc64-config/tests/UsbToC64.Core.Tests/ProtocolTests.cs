using System.Text;
using UsbToC64.Core.Device;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Simulated adapter: behaves like the firmware JSON engine (with or without get_jm).</summary>
internal sealed class FakeAdapter : IAdapterPort
{
    public bool SupportsGetJm { get; init; } = true;
    public bool Silent { get; init; }            // old firmware with a bad JSON: no reply at all
    public bool CorruptOnSave { get; init; }     // store something different from what was sent
    public bool RebootBeforeReply { get; init; } // saves, but the port vanishes before "ok" is read (seen on Windows)
    public bool ThrowOnSend { get; init; }       // ... and the send itself fails with "device not functioning"
    public bool NoComm { get; init; }            // nothing answers (wrong port, connected to the C64, dev mode)
    public bool ResetOnOpen { get; init; }       // opening the port restarts the chip (seen on Windows)
    public string? Version { get; init; }        // null = original firmware (no get_version)
    public int ResetAndDropOnOpen { get; set; }  // ... and the USB reconnection at boot kills the handle (n times)
    public JmProfile? Stored { get; private set; }
    public int Opens { get; private set; }
    private int _absentPolls;

    public string Name => "FAKE";

    public bool IsPresent()
    {
        if (_absentPolls > 0) { _absentPolls--; return false; }
        return true;
    }

    public IAdapterLink Open() { Opens++; return new Link(this); }

    private sealed class Link : IAdapterLink
    {
        private readonly FakeAdapter _a;
        private readonly Queue<string> _out = new();
        private readonly Queue<string> _boot = new();
        private bool _dropAfterBoot;
        public Link(FakeAdapter a)
        {
            _a = a;
            if (a.ResetAndDropOnOpen > 0)
            {
                a.ResetAndDropOnOpen--;
                _dropAfterBoot = true;
                foreach (var l in new[] { "load:0x403c8700,len:0xc2c", "load:0x403cb700,len:0x30c0", "entry 0x403c88b8" })
                    _boot.Enqueue(l);
            }
            else if (a.ResetOnOpen)
                foreach (var l in new[] { "ESP-ROM:esp32s3-20210327", "load:0x403c8700,len:0xc2c", "entry 0x403c88b8",
                                          "", "=================================", "  USB -> DB9 ADAPTER v1.2        ",
                                          "--- Booting: Configuration Mode ---", "=================================" })
                    _boot.Enqueue(l);
        }
        public string Name => "FAKE";
        public bool Lost { get; private set; }
        public void DiscardInput() => _out.Clear();
        public void Dispose() { }
        // Boot lines arrive over time: DiscardInput does not remove them, and commands are ignored until they are out.
        public string? ReadLine(TimeSpan timeout)
        {
            if (_boot.Count > 0) return _boot.Dequeue();
            if (_dropAfterBoot && !Lost) { Lost = true; _a._absentPolls = 3; } // USB reconnection: handle dead
            if (Lost) return null;
            return _out.Count > 0 ? _out.Dequeue() : null;
        }

        public void SendLine(string line)
        {
            if (Lost) throw new IOException("The port is closed.");
            if (_a.NoComm || _boot.Count > 0) return;
            if (line == "{\"cmd\":\"get_info\"}")
            {
                _out.Enqueue("{\"is_amiga\":false,\"dev_mode\":false,\"inv_switch\":false,\"dev_amiga\":false}");
                return;
            }
            if (line == "{\"cmd\":\"get_version\"}")
            {
                if (_a.Version != null) _out.Enqueue($"{{\"fw\":\"{_a.Version}\",\"build\":\"abc1234\",\"proto\":1}}");
                return;
            }
            if (line.StartsWith("{\"cmd\":\"save_jm\""))
            {
                if (_a.Silent) return;
                JmProfile p;
                try { p = SaveJmJson.Parse(line); }
                catch { _out.Enqueue("{\"status\":\"error\",\"reason\":\"json\",\"detail\":\"InvalidInput\"}"); return; }
                if (_a.CorruptOnSave && p.Rules.Count > 0) p.Rules[0] = p.Rules[0] with { Value = 0x7F };
                _a.Stored = p;
                _a._absentPolls = 3; // reboot
                if (_a.RebootBeforeReply) { Lost = true; if (_a.ThrowOnSend) throw new IOException("Un dispositivo collegato al sistema non è in funzione."); return; }
                _out.Enqueue("{\"status\":\"ok\"}");
            }
            else if (line == "{\"cmd\":\"get_jm\"}")
            {
                if (!_a.SupportsGetJm) return;
                if (_a.Stored == null) { _out.Enqueue("{\"status\":\"empty\",\"proto\":1}"); return; }
                var json = SaveJmJson.Serialize(_a.Stored).Replace("{\"cmd\":\"save_jm\",", "{\"status\":\"ok\",\"proto\":1,\"has_custom\":true,");
                _out.Enqueue(json);
            }
            else if (line == "dumppad")
            {
                foreach (var l in Dump(_a.Stored).Split('\n')) _out.Enqueue(l);
            }
        }
    }

    /// <summary>Same text as the firmware dumppad (ServiceTools.h).</summary>
    public static string Dump(JmProfile? p)
    {
        var sb = new StringBuilder();
        sb.Append("\n--- 🔍 NVS PAD DUMP ---\n");
        sb.Append($"has_custom   : {(p != null ? "TRUE" : "FALSE")}\n");
        if (p == null) { sb.Append(">>> NO PROFILE SAVED IN MEMORY <<<\n-----------------------\n"); return sb.ToString(); }
        sb.Append("\n[ TIER 0: HTML DYNAMIC RULES ]\n");
        sb.Append($"vid          : 0x{p.Vid:X4}\n");
        sb.Append($"pid          : 0x{p.Pid:X4}\n");
        sb.Append($"dpad_index   : {p.DpadIndex}\n");
        sb.Append($"rule_count   : {p.Rules.Count}\n");
        for (int i = 0; i < p.Rules.Count; i++)
        {
            var r = p.Rules[i];
            sb.Append($"  Rule {i + 1:00}    : Byte[{r.Index}] {Names.Op(r.Op),-3} {r.Value,3}  ->  Action: {Names.Func(r.Func)}\n");
        }
        sb.Append("jm_blob size : 146 bytes\n");
        sb.Append($"use_analog   : {(p.UseAnalog ? "YES" : "NO")}\n");
        if (p.UseAnalog)
        {
            sb.Append($"analog_x     : [{string.Join(", ", p.AnalogX)}]\n");
            sb.Append($"analog_y     : [{string.Join(", ", p.AnalogY)}]\n");
        }
        sb.Append("-----------------------\n");
        return sb.ToString();
    }
}

public class ProtocolTests
{
    private static JmProfile Sample()
    {
        var p = new JmProfile { Vid = 0x0810, Pid = 0x0001, DpadIndex = 5 };
        p.Rules.Add(new JmRule(5, 0, JmOp.Hat, JmFunc.Up));
        p.Rules.Add(new JmRule(5, 2, JmOp.Hat, JmFunc.Right));
        p.Rules.Add(new JmRule(5, 0x40, JmOp.BitAny, JmFunc.Fire1));
        p.Rules.Add(new JmRule(6, 0x02, JmOp.BitAny, JmFunc.AutofireHold));
        p.AnalogX.Add(3); p.AnalogY.Add(4);
        return p;
    }

    private static AdapterWriter Writer(FakeAdapter a, List<string>? log = null) =>
        new(a, s => log?.Add(s), _ => { }) { BootDelay = TimeSpan.Zero };

    [Fact]
    public void Write_NewFirmware_VerifiedWithGetJm()
    {
        var a = new FakeAdapter();
        var r = Writer(a).WriteAndVerify(Sample());
        Assert.True(r.Saved);
        Assert.True(r.Verified, r.Message + string.Join("\n", r.Differences));
        Assert.Equal(ReadMethod.GetJm, r.Method);
        Assert.Equal(2, a.Opens); // write, then read back after the reboot
    }

    [Fact]
    public void Write_OldFirmware_VerifiedWithDumppad()
    {
        var a = new FakeAdapter { SupportsGetJm = false };
        var r = Writer(a).WriteAndVerify(Sample());
        Assert.True(r.Verified, r.Message + string.Join("\n", r.Differences));
        Assert.Equal(ReadMethod.Dumppad, r.Method);
    }

    [Fact]
    public void Write_DifferentStoredProfile_IsReported()
    {
        var a = new FakeAdapter { CorruptOnSave = true };
        var r = Writer(a).WriteAndVerify(Sample());
        Assert.True(r.Saved);
        Assert.False(r.Verified);
        Assert.Single(r.Differences);
        Assert.Contains("Regola 1", r.Differences[0]);
    }

    [Fact]
    public void Write_PortVanishesBeforeOk_VerifiedByReadBack()
    {
        var log = new List<string>();
        var r = Writer(new FakeAdapter { RebootBeforeReply = true }, log).WriteAndVerify(Sample());
        Assert.True(r.Verified, r.Message);
        Assert.Contains(log, l => l.Contains("Probabile riavvio"));
    }

    [Fact]
    public void Write_SendFailsBecauseOfReboot_VerifiedByReadBack()
    {
        var log = new List<string>();
        var r = Writer(new FakeAdapter { RebootBeforeReply = true, ThrowOnSend = true }, log).WriteAndVerify(Sample());
        Assert.True(r.Verified, r.Message);
        Assert.Contains(log, l => l.Contains("scomparsa durante l'invio"));
    }

    [Fact]
    public void Write_NoCommunication_NothingIsSent()
    {
        var a = new FakeAdapter { NoComm = true };
        var log = new List<string>();
        var r = Writer(a, log).WriteAndVerify(Sample());
        Assert.False(r.Saved);
        Assert.StartsWith("Profilo NON inviato", r.Message);
        Assert.DoesNotContain(log, l => l.Contains("save_jm"));
    }

    [Fact]
    public void Write_AdapterRestartsOnOpen_WaitsForBootAndSucceeds()
    {
        var log = new List<string>();
        var r = Writer(new FakeAdapter { ResetOnOpen = true }, log).WriteAndVerify(Sample());
        Assert.True(r.Verified, r.Message);
        Assert.Contains(log, l => l.Contains("Comunicazione OK"));
    }

    [Fact]
    public void Write_ResetAndUsbReconnectOnOpen_ReopensLikeATerminal()
    {
        // Seen on the real adapter: ROM boot lines, then "The port is closed".
        var a = new FakeAdapter { ResetAndDropOnOpen = 1 };
        var log = new List<string>();
        var r = Writer(a, log).WriteAndVerify(Sample());
        Assert.True(r.Verified, r.Message);
        Assert.Contains(log, l => l.Contains("riapro"));
        Assert.Contains(log, l => l.Contains("tentativo 2"));
    }

    [Fact]
    public void Ping_GivesUpAfterThreeRestarts()
    {
        var a = new FakeAdapter { ResetAndDropOnOpen = 99 };
        Assert.Throws<TimeoutException>(() =>
            AdapterConnection.Connect(a, null, _ => { }, bootDelay: TimeSpan.Zero));
        Assert.Equal(3, a.Opens);
    }

    [Fact]
    public void Write_NoReply_IsNotReportedAsSaved()
    {
        var r = Writer(new FakeAdapter { Silent = true }).WriteAndVerify(Sample());
        Assert.False(r.Saved);
        Assert.Contains("Nessuna risposta", r.Message);
    }

    [Fact]
    public void DumppadParser_ReadsRealOutput()
    {
        // Output of the real adapter after the manual test (see conversation, prova B).
        const string text = """
            --- 🔍 NVS PAD DUMP ---
            has_custom   : TRUE

            [ TIER 0: HTML DYNAMIC RULES ]
            vid          : 0x045E
            pid          : 0x028E
            dpad_index   : -1
            rule_count   : 1
              Rule 01    : Byte[10] ==    1  ->  Action: FIRE 1
            jm_blob size : 146 bytes
            use_analog   : NO
            -----------------------
            """;
        var p = DumppadParser.Parse(text)!;
        Assert.Equal((0x045E, 0x028E, -1), ((int)p.Vid, (int)p.Pid, (int)p.DpadIndex));
        Assert.Equal(new JmRule(10, 1, JmOp.Eq, JmFunc.Fire1), Assert.Single(p.Rules));
        Assert.False(p.UseAnalog);
    }

    [Fact]
    public void DumppadParser_RoundTripsAllFunctions()
    {
        var p = new JmProfile { Vid = 1, Pid = 2 };
        foreach (var f in Enum.GetValues<JmFunc>()) p.Rules.Add(new JmRule((byte)f, 255, JmOp.BitAny, f));
        p.AnalogX.AddRange(new byte[] { 1, 2 }); p.AnalogY.Add(3);
        var q = DumppadParser.Parse(FakeAdapter.Dump(p))!;
        Assert.Empty(ProfileComparer.Compare(p, q));
    }

    [Fact]
    public void Compare_DetectsTruncationTo32Rules()
    {
        var sent = new JmProfile { Vid = 1, Pid = 2 };
        for (int i = 0; i < 34; i++) sent.Rules.Add(new JmRule((byte)i, 1, JmOp.BitAny, JmFunc.Fire1));
        var stored = new JmProfile { Vid = 1, Pid = 2 };
        stored.Rules.AddRange(sent.Rules.Take(32));
        var d = ProfileComparer.Compare(sent, stored);
        Assert.Single(d);
        Assert.Contains("32", d[0]);
    }
}
