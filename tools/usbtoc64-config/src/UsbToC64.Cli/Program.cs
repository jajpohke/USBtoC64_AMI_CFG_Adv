using System.Text;
using UsbToC64.Core.Device;
using UsbToC64.Core.Emulation;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Core.Validation;
using UsbToC64.Device;
using UsbToC64.Input;

namespace UsbToC64.Cli;

internal static class Program
{
    private static string Usage => L.T("cli.usage");

    private static int Main(string[] args)
    {
        L.Add(CliStrings.All);
        Console.OutputEncoding = Encoding.UTF8;
        args = SelectLanguage(args);
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }
        try
        {
            return args[0] switch
            {
                "devices" => Devices(),
                "analyze" => Analyze(args[1..]),
                "monitor" => Monitor(args[1..]),
                "check" => Check(args[1..]),
                "convert" => Convert(args[1..]),
                "descriptor" => Descriptor(args[1..]),
                "ports" => Ports(),
                "ping" => Ping(args[1..]),
                "version" => Version(args[1..]),
                "flash" => Flash(args[1..]),
                "write" => Write(args[1..]),
                "read" => Read(args[1..]),
                "slots" => Slots(args[1..]),
                "slot-rename" => SlotRename(args[1..]),
                "slot-delete" => SlotDelete(args[1..]),
                _ => Fail(L.F("cli.unknownCommand", args[0]) + "\n\n" + Usage),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
        {
            return Fail(ex.Message);
        }
    }

    // ------------------------------------------------------------------ commands

    private static int Devices()
    {
        var list = PadDevices.List();
        if (list.Count == 0)
        {
            Console.WriteLine(L.T("cli.noPads"));
            return 1;
        }
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            Console.WriteLine($"[{i}] {d}");
            if (d.RawDescriptor == null && !d.IsXInput) Console.WriteLine("     " + L.F("cli.descriptorUnreadable", d.DescriptorError));
        }
        var slots = PadReader.ConnectedXInputSlots();
        if (slots.Count > 0) Console.WriteLine(L.F("cli.xinputSlots", string.Join(", ", slots)));
        return 0;
    }

    private static int Analyze(string[] args)
    {
        var device = PickDevice(args);
        var pad = device.Describe();
        var mapping = MappingProposer.Propose(pad);
        ApplySets(mapping, args);

        Console.WriteLine(L.F("cli.pad", device));
        if (HasFlag(args, "--dump-descriptor") && device.RawDescriptor != null)
            Console.WriteLine(L.F("cli.descriptorDump", device.RawDescriptor.Length) + $"\n{ToHex(device.RawDescriptor)}\n");

        var profile = ProfileBuilder.Build(mapping, device.Vid, device.Pid, device.Name);
        PrintMapping(mapping);
        PrintProfile(profile, null);

        if (Option(args, "-o") is { } outFile)
        {
            File.WriteAllText(outFile, SaveJmJson.Serialize(profile) + "\n");
            Console.WriteLine("\n" + L.F("cli.saved", outFile));
        }
        return 0;
    }

    private static int Monitor(string[] args)
    {
        var device = PickDevice(args);
        var pad = device.Describe();
        JmProfile profile;
        PadMapping? mapping = null;
        if (Option(args, "--profile") is { } file)
        {
            profile = LoadProfile(file).Profile;
            Console.WriteLine(L.F("cli.profileFile", file));
        }
        else
        {
            mapping = MappingProposer.Propose(pad);
            ApplySets(mapping, args);
            profile = ProfileBuilder.Build(mapping, device.Vid, device.Pid, device.Name);
            Console.WriteLine(L.T("cli.profileAuto"));
        }

        var engine = new DynamicEngine(profile);
        int slot = PadReader.ConnectedXInputSlots().FirstOrDefault();
        using var reader = new PadReader(device, pad, slot);
        Console.WriteLine(L.F("cli.monitor.start", device) + "\n");

        string lastLine = "";
        while (!Console.KeyAvailable)
        {
            var report = reader.Read();
            if (report == null) continue;
            var state = engine.Process(report);
            if (state == null) continue;
            var active = pad.Controls.Where(c => c.IsActive(report)).Select(c => c.Name);
            string line = L.F("cli.monitor.line", state.Value, string.Join(", ", active));
            if (line == lastLine) continue;
            lastLine = line;
            Console.Write("\r" + line.PadRight(Math.Max(Console.WindowWidth - 1, line.Length)));
        }
        Console.ReadKey(true);
        Console.WriteLine();
        return 0;
    }

    private static int Check(string[] args)
    {
        if (args.Length < 1) return Fail(L.T("cli.check.needFile"));
        var (profile, header) = LoadProfile(args[0]);
        if (header != null)
        {
            Console.WriteLine(L.F("cli.generator", header.Generator));
            foreach (var n in header.Notes) Console.WriteLine(L.F("cli.note", n));
        }
        return PrintProfile(profile, header) ? 0 : 2;
    }

    private static int Convert(string[] args)
    {
        if (args.Length < 1) return Fail(L.T("cli.convert.needFile"));
        var (profile, header) = LoadProfile(args[0]);
        bool ok = PrintProfile(profile, header);
        string json = SaveJmJson.Serialize(profile);
        if (Option(args, "-o") is { } outFile)
        {
            File.WriteAllText(outFile, json + "\n");
            Console.WriteLine("\n" + L.F("cli.saved", outFile));
        }
        else Console.WriteLine($"\n{json}");
        return ok ? 0 : 2;
    }

    private static int Descriptor(string[] args)
    {
        if (args.Length < 1) return Fail(L.T("cli.descriptor.needFile"));
        var text = File.ReadAllText(args[0]);
        var bytes = text.Split((char[])[' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => System.Convert.ToByte(h.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16)).ToArray();
        var pad = ControlClassifier.Classify(bytes);
        var mapping = MappingProposer.Propose(pad);
        ApplySets(mapping, args);
        PrintMapping(mapping);
        PrintProfile(ProfileBuilder.Build(mapping, 0, 0), null, skipVidPid: true);
        return 0;
    }

    private static int Ports()
    {
        var ports = AdapterPorts.List();
        if (ports.Count == 0) { Console.WriteLine(L.T("cli.ports.none")); return 1; }
        foreach (var p in ports) Console.WriteLine($"{(p.IsEspressif ? "*" : " ")} {p}");
        Console.WriteLine(L.T("cli.ports.legend"));
        return 0;
    }

    private static int Write(string[] args)
    {
        if (args.Length < 1 || args[0].StartsWith('-')) return Fail(L.T("cli.write.needFile"));
        var (profile, header) = LoadProfile(args[0]);
        if (!PrintProfile(profile, header))
            return Fail("\n" + L.T("cli.write.hasErrors"));
        var port = AdapterPorts.Find(Option(args, "--port"));
        Console.WriteLine("\n" + L.F("cli.write.adapter", port));
        var writer = new AdapterWriter(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var r = writer.WriteAndVerify(profile, SlotOption(args), Option(args, "--label"));
        Console.WriteLine($"\n{r.Message}");
        foreach (var d in r.Differences) Console.WriteLine($"  ≠ {d}");
        return r.Verified ? 0 : r.Saved ? 3 : 1;
    }

    private static int Ping(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var info = conn.Info;
        Console.WriteLine(L.F("cli.ping.ok", port.PortName, info));
        return 0;
    }

    private static FirmwareManifest? Manifest() => FirmwareManifest.FindNextTo(AppContext.BaseDirectory);

    private static int Version(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var fw = conn.Session.GetFirmware();
        Console.WriteLine(L.F("cli.version.adapter", port.PortName, fw));
        var m = Manifest();
        if (m == null) { Console.WriteLine(L.T("cli.version.noFirmware")); return 0; }
        Console.WriteLine(L.F("cli.version.included", m.Version, m.Build));
        Console.WriteLine(FirmwareCheck.Compare(fw, m) switch
        {
            FirmwareStatus.UpdateAvailable => L.T("cli.version.updateAvailable"),
            FirmwareStatus.OtherBuild => L.F("cli.version.otherBuild", fw.Build, m.Build),
            FirmwareStatus.UpToDate => L.T("cli.version.upToDate"),
            _ => L.T("cli.version.newerOnAdapter"),
        });
        return 0;
    }

    private static int Flash(string[] args)
    {
        var m = Manifest() ?? throw new InvalidOperationException(L.T("cli.flash.noFirmware"));
        var port = AdapterPorts.Find(Option(args, "--port"));
        Console.WriteLine($"Firmware {m.Version} (build {m.Build}) → {port}");
        Console.WriteLine(L.T("cli.flash.warning"));
        if (!HasFlag(args, "--yes"))
        {
            Console.Write(L.T("cli.flash.prompt"));
            if (!string.Equals(Console.ReadLine()?.Trim(), L.T("cli.flash.yesWord"), StringComparison.OrdinalIgnoreCase)) return Fail(L.T("cli.flash.cancelled"));
        }
        var r = new FirmwareUpdater(s => Console.WriteLine(s)).Update(port.PortName, m);
        Console.WriteLine(r.Message);
        return r.Success ? 0 : 1;
    }

    private static int Read(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        bool json = HasFlag(args, "--json");
        using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), json ? null : s => Console.WriteLine("  " + s));
        StoredProfile stored;
        if (SlotOption(args) is { } slot)
        {
            if (!SlotCommands.Supports(conn.Session.GetFirmware()))
                return Fail(L.F("cli.slots.needFirmware", FirmwareLimits.SlotsMinFirmware));
            var d = conn.Session.ReadSlot(slot);
            if (d != null && !json) Console.WriteLine(L.F("cli.slots.reading", d.Info.Number, d.Info.DisplayLabel));
            stored = new StoredProfile(d?.Profile, ReadMethod.GetJm, d != null);
        }
        else stored = conn.Session.ReadStored();
        if (stored.Profile == null)
        {
            Console.WriteLine(L.T("cli.read.none"));
            return 1;
        }
        if (json) { Console.WriteLine(SaveJmJson.Serialize(stored.Profile)); return 0; }
        Console.WriteLine("\n" + L.F("cli.read.via", stored.Method == ReadMethod.GetJm ? "get_jm" : L.T("cli.read.viaDumppad")));
        PrintProfile(stored.Profile, null);
        return 0;
    }

    /// <summary>--slot n (1..8, as shown to the user) → 0-based drawer.</summary>
    private static int? SlotOption(string[] args)
    {
        if (Option(args, "--slot") is not { } s) return null;
        if (!int.TryParse(s, out int n) || n < 1 || n > FirmwareLimits.MaxSlots)
            throw new ArgumentException(L.F("cli.slots.badNumber", s, FirmwareLimits.MaxSlots));
        return n - 1;
    }

    private static AdapterConnection ConnectForSlots(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        if (!SlotCommands.Supports(conn.Session.GetFirmware()))
        {
            conn.Dispose();
            throw new InvalidOperationException(L.F("cli.slots.needFirmware", FirmwareLimits.SlotsMinFirmware));
        }
        return conn;
    }

    private static int Slots(string[] args)
    {
        using var conn = ConnectForSlots(args);
        var list = conn.Session.ListSlots();
        Console.WriteLine("\n" + L.F("cli.slots.header", list.Count, FirmwareLimits.MaxSlots));
        for (int i = 0; i < FirmwareLimits.MaxSlots; i++)
        {
            var s = list.FirstOrDefault(x => x.Slot == i);
            Console.WriteLine(s == null
                ? $"  [{i + 1}] {L.T("cli.slots.empty")}"
                : $"  [{s.Number}] {s.DisplayLabel,-24}  {s.Ids}  {L.F("cli.slots.rules", s.RuleCount)}{(s.Analog ? " + " + L.T("cli.slots.analog") : "")}");
        }
        return 0;
    }

    private static int SlotRename(string[] args)
    {
        if (args.Length < 2 || args[0].StartsWith('-')) return Fail(L.T("cli.slots.renameUsage"));
        int slot = SlotOption(new[] { "--slot", args[0] })!.Value;
        using var conn = ConnectForSlots(args);
        conn.Session.RenameSlot(slot, args[1]);
        var d = conn.Session.ReadSlot(slot);
        Console.WriteLine(L.F("cli.slots.renamed", slot + 1, d?.Info.DisplayLabel ?? ""));
        return 0;
    }

    private static int SlotDelete(string[] args)
    {
        if (args.Length < 1 || args[0].StartsWith('-')) return Fail(L.T("cli.slots.deleteUsage"));
        int slot = SlotOption(new[] { "--slot", args[0] })!.Value;
        using var conn = ConnectForSlots(args);
        var d = conn.Session.ReadSlot(slot);
        if (d == null) return Fail(L.F("cli.slots.isEmpty", slot + 1));
        if (!HasFlag(args, "--yes"))
        {
            Console.Write(L.F("cli.slots.deletePrompt", d.Info.Number, d.Info.DisplayLabel, d.Info.Ids));
            if (!string.Equals(Console.ReadLine()?.Trim(), L.T("cli.flash.yesWord"), StringComparison.OrdinalIgnoreCase))
                return Fail(L.T("cli.flash.cancelled"));
        }
        conn.Session.DeleteSlot(slot);
        Console.WriteLine(L.F("cli.slots.deleted", d.Info.Number, d.Info.DisplayLabel));
        return 0;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>--lang &lt;en|it|es|de&gt; (removed from the arguments), else USBTOC64_LANG, else English.</summary>
    private static string[] SelectLanguage(string[] args)
    {
        string? lang = Environment.GetEnvironmentVariable("USBTOC64_LANG");
        var rest = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--lang" && i + 1 < args.Length) { lang = args[++i]; continue; }
            if (args[i].StartsWith("--lang=", StringComparison.Ordinal)) { lang = args[i]["--lang=".Length..]; continue; }
            rest.Add(args[i]);
        }
        L.Set(lang);
        return rest.ToArray();
    }

    private static (JmProfile Profile, ImportedHeader? Header) LoadProfile(string file)
    {
        var text = File.ReadAllText(file);
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{'))
            return (SaveJmJson.Parse(text.Trim()), null);
        var h = HeaderImporter.Import(text);
        return (h.Profile, h);
    }

    private static PadDevice PickDevice(string[] args)
    {
        var list = PadDevices.List();
        if (list.Count == 0) throw new InvalidOperationException(L.T("cli.noPads"));
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            if (list.Count == 1) return list[0];
            throw new ArgumentException(L.T("cli.pickDevice.many"));
        }
        if (!int.TryParse(args[0], out int n) || n < 0 || n >= list.Count)
            throw new ArgumentException(L.F("cli.pickDevice.invalid", args[0]));
        return list[n];
    }

    private static void ApplySets(PadMapping mapping, string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--set") continue;
            var kv = args[i + 1].Split('=', 2);
            if (kv.Length != 2) throw new ArgumentException(L.F("cli.set.format", args[i + 1]));
            var control = mapping.Pad.Controls.FirstOrDefault(c => string.Equals(c.Id, kv[0], StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(L.F("cli.set.unknownControl", kv[0]));
            if (!Enum.TryParse<PadAction>(kv[1], true, out var action))
                throw new ArgumentException(L.F("cli.set.unknownAction", kv[1]));
            mapping.Set(control, action);
        }
    }

    private static void PrintMapping(PadMapping m)
    {
        foreach (var n in m.Pad.Notes) Console.WriteLine(L.F("cli.note", n));
        Console.WriteLine("\n" + L.T("cli.controls.title"));
        Console.WriteLine($"  {"id",-12} {L.T("cli.col.name"),-26} {L.T("cli.col.kind"),-10} {"byte",4}  {L.T("cli.col.mask"),-8} {L.T("cli.col.action")}");
        foreach (var c in m.Pad.Controls)
        {
            string mask = c.Kind switch
            {
                ControlKind.Hat => c.HatBase == 0 ? "hat 0-7" : $"hat {c.HatBase}-{c.HatBase + c.HatPositions - 1}",
                ControlKind.Axis => L.T("cli.kind.axis"),
                _ => $"0x{c.Mask:X2}",
            };
            string action = c.Supported ? PadActions.Label(m.Get(c)) : L.T("cli.unsupported");
            Console.WriteLine($"  {c.Id,-12} {c.Name,-26} {KindLabel(c.Kind),-10} {c.ByteIndex,4}  {mask,-8} {action}");
            if (c.Note != null) Console.WriteLine($"  {"",-12} -> {c.Note}");
        }
    }

    /// <returns>false when there are errors.</returns>
    private static bool PrintProfile(JmProfile p, ImportedHeader? header, bool skipVidPid = false)
    {
        Console.WriteLine("\n" + L.F("cli.profile.header", p.Vid.ToString("X4"), p.Pid.ToString("X4"), p.DeviceName != null ? $" ({p.DeviceName})" : ""));
        Console.WriteLine(L.F("cli.profile.rules", p.Rules.Count, FirmwareLimits.MaxRules));
        for (int i = 0; i < p.Rules.Count; i++) Console.WriteLine($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
        if (p.UseAnalog)
            Console.WriteLine(L.F("cli.profile.axes", string.Join(", ", p.AnalogX), string.Join(", ", p.AnalogY)));

        var issues = ProfileValidator.Validate(p, header);
        if (skipVidPid) issues.RemoveAll(i => i.Code == "NO_VIDPID");
        Console.WriteLine("\n" + L.T(issues.Count == 0 ? "cli.checks.none" : "cli.checks.title"));
        foreach (var i in issues.OrderByDescending(i => i.Severity)) Console.WriteLine($"  {i}");
        return issues.All(i => i.Severity != Severity.Error);
    }

    private static string KindLabel(ControlKind k) => k switch
    {
        ControlKind.Button => L.T("cli.kind.button"),
        ControlKind.DpadButton => "D-pad",
        ControlKind.Hat => "hat",
        ControlKind.Axis => L.T("cli.kind.axis"),
        _ => k.ToString(),
    };

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static bool HasFlag(string[] args, string name) => args.Contains(name);

    private static string ToHex(byte[] b) =>
        string.Join("\n", b.Chunk(16).Select(c => string.Join(" ", c.Select(x => x.ToString("X2")))));

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }
}
