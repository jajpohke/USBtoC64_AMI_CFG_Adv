using System.Text;
using UsbToC64.Core.Device;
using UsbToC64.Core.Emulation;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using UsbToC64.Device;
using UsbToC64.Input;

namespace UsbToC64.Cli;

internal static class Program
{
    private const string Usage = """
        usbtoc64 - preparazione profili gamepad per l'adattatore USBtoC64

        Comandi:
          devices                          Elenca i gamepad collegati al PC
          analyze <n> [opzioni]            Classifica i controlli del pad n e propone la mappatura
              --set <id>=<azione>          Cambia l'azione di un controllo (ripetibile)
              -o <file.json>               Salva il comando save_jm in un file
              --dump-descriptor            Stampa il descrittore HID in esadecimale
          monitor <n> [opzioni]            Anteprima dal vivo: cosa riceve il C64/Amiga su porta DB9
              --profile <file.json|.h>     Usa questo profilo invece della proposta automatica
              --set <id>=<azione>          Come per analyze
          check <file.h|file.json>         Legge un profilo esistente e lo controlla
          convert <file.h> [-o out.json]   Converte un JoystickMapping.h nel JSON save_jm
          descriptor <file>                Analizza un descrittore HID salvato come testo esadecimale

        Adattatore (collegato al PC con la USB-C, non al C64/Amiga, Developer Mode spenta):
          ports                            Elenca le porte seriali (l'ESP32 è riconosciuto da solo)
          ping [--port COMx]               Verifica che l'adattatore risponda (get_info)
          version [--port COMx]            Firmware sull'adattatore e firmware incluso nel programma
          flash [--port COMx] [--yes]      Installa il firmware incluso (CANCELLA TUTTE LE IMPOSTAZIONI)
          write <file.json|.h> [--port COMx]   Scrive il profilo, attende il riavvio e lo rilegge per verifica
          read [--port COMx] [--json]      Legge il profilo pad salvato sull'adattatore

        Azioni: None, Fire1, Fire2, Fire3, AutofireOn, AutofireOff, AutofireHold,
                Up, Down, Left, Right, UpRight, RightDown, DownLeft, LeftUp,
                Directions (hat), AxisX, AxisY (assi)
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
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
                _ => Fail($"Comando sconosciuto: {args[0]}\n\n{Usage}"),
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
            Console.WriteLine("Nessun gamepad trovato.");
            return 1;
        }
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            Console.WriteLine($"[{i}] {d}");
            if (d.RawDescriptor == null && !d.IsXInput) Console.WriteLine($"     descrittore non leggibile: {d.DescriptorError}");
        }
        var slots = PadReader.ConnectedXInputSlots();
        if (slots.Count > 0) Console.WriteLine($"Slot XInput collegati: {string.Join(", ", slots)}");
        return 0;
    }

    private static int Analyze(string[] args)
    {
        var device = PickDevice(args);
        var pad = device.Describe();
        var mapping = MappingProposer.Propose(pad);
        ApplySets(mapping, args);

        Console.WriteLine($"Pad: {device}");
        if (HasFlag(args, "--dump-descriptor") && device.RawDescriptor != null)
            Console.WriteLine($"Descrittore ({device.RawDescriptor.Length} byte):\n{ToHex(device.RawDescriptor)}\n");

        var profile = ProfileBuilder.Build(mapping, device.Vid, device.Pid, device.Name);
        PrintMapping(mapping);
        PrintProfile(profile, null);

        if (Option(args, "-o") is { } outFile)
        {
            File.WriteAllText(outFile, SaveJmJson.Serialize(profile) + "\n");
            Console.WriteLine($"\nSalvato: {outFile}");
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
            Console.WriteLine($"Profilo: {file}");
        }
        else
        {
            mapping = MappingProposer.Propose(pad);
            ApplySets(mapping, args);
            profile = ProfileBuilder.Build(mapping, device.Vid, device.Pid, device.Name);
            Console.WriteLine("Profilo: proposta automatica");
        }

        var engine = new DynamicEngine(profile);
        int slot = PadReader.ConnectedXInputSlots().FirstOrDefault();
        using var reader = new PadReader(device, pad, slot);
        Console.WriteLine($"Pad: {device}. Muovi la leva e premi i tasti; un tasto qualsiasi sulla tastiera per uscire.\n");

        string lastLine = "";
        while (!Console.KeyAvailable)
        {
            var report = reader.Read();
            if (report == null) continue;
            var state = engine.Process(report);
            if (state == null) continue;
            var active = pad.Controls.Where(c => c.IsActive(report)).Select(c => c.Name);
            string line = $"C64/Amiga su porta DB9: {state.Value}   pad: {string.Join(", ", active)}";
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
        if (args.Length < 1) return Fail("Specifica un file .h o .json");
        var (profile, header) = LoadProfile(args[0]);
        if (header != null)
        {
            Console.WriteLine($"Generatore: {header.Generator}");
            foreach (var n in header.Notes) Console.WriteLine($"Nota: {n}");
        }
        return PrintProfile(profile, header) ? 0 : 2;
    }

    private static int Convert(string[] args)
    {
        if (args.Length < 1) return Fail("Specifica un file .h");
        var (profile, header) = LoadProfile(args[0]);
        bool ok = PrintProfile(profile, header);
        string json = SaveJmJson.Serialize(profile);
        if (Option(args, "-o") is { } outFile)
        {
            File.WriteAllText(outFile, json + "\n");
            Console.WriteLine($"\nSalvato: {outFile}");
        }
        else Console.WriteLine($"\n{json}");
        return ok ? 0 : 2;
    }

    private static int Descriptor(string[] args)
    {
        if (args.Length < 1) return Fail("Specifica il file con il descrittore in esadecimale");
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
        if (ports.Count == 0) { Console.WriteLine("Nessuna porta seriale."); return 1; }
        foreach (var p in ports) Console.WriteLine($"{(p.IsEspressif ? "*" : " ")} {p}");
        Console.WriteLine("(* = ESP32, scelto in automatico)");
        return 0;
    }

    private static int Write(string[] args)
    {
        if (args.Length < 1 || args[0].StartsWith('-')) return Fail("Specifica il file del profilo (.json o .h)");
        var (profile, header) = LoadProfile(args[0]);
        if (!PrintProfile(profile, header))
            return Fail("\nIl profilo ha errori: non lo scrivo. Correggilo prima (o usa il JSON solo per prova).");
        var port = AdapterPorts.Find(Option(args, "--port"));
        Console.WriteLine($"\nAdattatore: {port}");
        var writer = new AdapterWriter(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var r = writer.WriteAndVerify(profile);
        Console.WriteLine($"\n{r.Message}");
        foreach (var d in r.Differences) Console.WriteLine($"  ≠ {d}");
        return r.Verified ? 0 : r.Saved ? 3 : 1;
    }

    private static int Ping(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var info = conn.Info;
        Console.WriteLine($"L'adattatore su {port.PortName} risponde: {info}.");
        return 0;
    }

    private static FirmwareManifest? Manifest() => FirmwareManifest.FindNextTo(AppContext.BaseDirectory);

    private static int Version(string[] args)
    {
        var port = AdapterPorts.Find(Option(args, "--port"));
        using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), s => Console.WriteLine("  " + s));
        var fw = conn.Session.GetFirmware();
        Console.WriteLine($"Adattatore su {port.PortName}: {fw}");
        var m = Manifest();
        if (m == null) { Console.WriteLine("Nessun firmware incluso (manca firmware.json)."); return 0; }
        Console.WriteLine($"Firmware incluso: {m.Version} (build {m.Build})");
        Console.WriteLine(FirmwareCheck.Compare(fw, m) switch
        {
            FirmwareStatus.UpdateAvailable => "→ aggiornamento disponibile: usbtoc64 flash",
            FirmwareStatus.UpToDate => "→ aggiornato",
            _ => "→ sull'adattatore c'è un firmware più recente",
        });
        return 0;
    }

    private static int Flash(string[] args)
    {
        var m = Manifest() ?? throw new InvalidOperationException("Nessun firmware incluso (manca firmware.json accanto al programma).");
        var port = AdapterPorts.Find(Option(args, "--port"));
        Console.WriteLine($"Firmware {m.Version} (build {m.Build}) → {port}");
        Console.WriteLine("ATTENZIONE: l'aggiornamento cancella TUTTE le impostazioni dell'adattatore (profilo pad, mouse, LED, Developer Mode).");
        if (!HasFlag(args, "--yes"))
        {
            Console.Write("Scrivi SI per continuare: ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "SI", StringComparison.OrdinalIgnoreCase)) return Fail("Annullato.");
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
        var stored = conn.Session.ReadStored();
        if (stored.Profile == null)
        {
            Console.WriteLine("Nessun profilo pad (TIER 0) salvato sull'adattatore.");
            return 1;
        }
        if (json) { Console.WriteLine(SaveJmJson.Serialize(stored.Profile)); return 0; }
        Console.WriteLine($"\nLetto con {(stored.Method == ReadMethod.GetJm ? "get_jm" : "dumppad (firmware senza get_jm)")}:");
        PrintProfile(stored.Profile, null);
        return 0;
    }

    // ------------------------------------------------------------------ helpers

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
        if (list.Count == 0) throw new InvalidOperationException("Nessun gamepad trovato.");
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            if (list.Count == 1) return list[0];
            throw new ArgumentException("Più gamepad collegati: indica il numero (vedi 'devices').");
        }
        if (!int.TryParse(args[0], out int n) || n < 0 || n >= list.Count)
            throw new ArgumentException($"Numero pad non valido: {args[0]} (vedi 'devices').");
        return list[n];
    }

    private static void ApplySets(PadMapping mapping, string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--set") continue;
            var kv = args[i + 1].Split('=', 2);
            if (kv.Length != 2) throw new ArgumentException($"--set richiede id=azione: {args[i + 1]}");
            var control = mapping.Pad.Controls.FirstOrDefault(c => string.Equals(c.Id, kv[0], StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Controllo sconosciuto: {kv[0]}");
            if (!Enum.TryParse<PadAction>(kv[1], true, out var action))
                throw new ArgumentException($"Azione sconosciuta: {kv[1]}");
            mapping.Set(control, action);
        }
    }

    private static void PrintMapping(PadMapping m)
    {
        foreach (var n in m.Pad.Notes) Console.WriteLine($"Nota: {n}");
        Console.WriteLine("\nControlli (classificati automaticamente):");
        Console.WriteLine($"  {"id",-12} {"nome",-26} {"tipo",-10} {"byte",4}  {"maschera",-8} azione");
        foreach (var c in m.Pad.Controls)
        {
            string mask = c.Kind switch
            {
                ControlKind.Hat => c.HatBase == 0 ? "hat 0-7" : $"hat {c.HatBase}-{c.HatBase + c.HatPositions - 1}",
                ControlKind.Axis => "asse",
                _ => $"0x{c.Mask:X2}",
            };
            string action = c.Supported ? PadActions.Label(m.Get(c)) : "NON SUPPORTATO";
            Console.WriteLine($"  {c.Id,-12} {c.Name,-26} {KindLabel(c.Kind),-10} {c.ByteIndex,4}  {mask,-8} {action}");
            if (c.Note != null) Console.WriteLine($"  {"",-12} -> {c.Note}");
        }
    }

    /// <returns>false when there are errors.</returns>
    private static bool PrintProfile(JmProfile p, ImportedHeader? header, bool skipVidPid = false)
    {
        Console.WriteLine($"\nProfilo: VID {p.Vid:X4} PID {p.Pid:X4}{(p.DeviceName != null ? $" ({p.DeviceName})" : "")}");
        Console.WriteLine($"Regole ({p.Rules.Count}/{FirmwareLimits.MaxRules}):");
        for (int i = 0; i < p.Rules.Count; i++) Console.WriteLine($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
        if (p.UseAnalog)
            Console.WriteLine($"Assi: X [{string.Join(", ", p.AnalogX)}]  Y [{string.Join(", ", p.AnalogY)}]");

        var issues = ProfileValidator.Validate(p, header);
        if (skipVidPid) issues.RemoveAll(i => i.Code == "NO_VIDPID");
        Console.WriteLine(issues.Count == 0 ? "\nControlli: nessun problema." : "\nControlli:");
        foreach (var i in issues.OrderByDescending(i => i.Severity)) Console.WriteLine($"  {i}");
        return issues.All(i => i.Severity != Severity.Error);
    }

    private static string KindLabel(ControlKind k) => k switch
    {
        ControlKind.Button => "pulsante",
        ControlKind.DpadButton => "D-pad",
        ControlKind.Hat => "hat",
        ControlKind.Axis => "asse",
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
