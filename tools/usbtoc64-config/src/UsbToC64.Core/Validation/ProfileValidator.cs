using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using UsbToC64.Core.XInput;

namespace UsbToC64.Core.Validation;

public enum Severity { Info, Warning, Error }

public sealed record Issue(Severity Severity, string Code, string Message)
{
    public override string ToString() => $"[{Severity switch { Severity.Error => "ERRORE", Severity.Warning => "AVVISO", _ => "INFO" }}] {Code}: {Message}";
}

/// <summary>
/// Checks a profile against the firmware contract (DESIGN.md §2) and the problems found in real profiles (P4-P9).
/// </summary>
public static class ProfileValidator
{
    /// <param name="header">When the profile comes from a .h file: enables the checks on the compiled-header semantics.</param>
    public static List<Issue> Validate(JmProfile p, ImportedHeader? header = null)
    {
        var issues = new List<Issue>();
        void Add(Severity s, string code, string msg) => issues.Add(new Issue(s, code, msg));

        // --- Firmware contract -------------------------------------------------------------
        if (p.IsEmpty)
            Add(Severity.Error, "EMPTY", "Profilo vuoto: nessuna regola e nessun asse. Il pad non risponderebbe.");
        if (p.Vid == 0 || p.Pid == 0)
            Add(Severity.Error, "NO_VIDPID", $"VID/PID mancanti ({p.Vid:X4}:{p.Pid:X4}): il firmware attiva il profilo solo se coincidono con il pad collegato.");
        if (p.Rules.Count > FirmwareLimits.MaxRules)
            Add(Severity.Error, "TOO_MANY_RULES", $"{p.Rules.Count} regole: il firmware ne salva al massimo {FirmwareLimits.MaxRules}, le altre vengono scartate.");
        if (p.AnalogX.Count > FirmwareLimits.MaxAnalogPerAxis || p.AnalogY.Count > FirmwareLimits.MaxAnalogPerAxis)
            Add(Severity.Error, "TOO_MANY_AXES", $"Al massimo {FirmwareLimits.MaxAnalogPerAxis} assi per direzione.");

        int est = SaveJmJson.EstimateArduinoJsonBytes(p);
        if (est > FirmwareLimits.JsonDocumentCapacity)
            Add(Severity.Error, "JSON_TOO_BIG", $"JSON stimato ~{est} byte oltre il buffer del firmware ({FirmwareLimits.JsonDocumentCapacity}): il salvataggio fallirebbe senza risposta.");
        else if (est > FirmwareLimits.JsonDocumentCapacity * 9 / 10)
            Add(Severity.Warning, "JSON_NEAR_LIMIT", $"JSON stimato ~{est} byte, vicino al limite di {FirmwareLimits.JsonDocumentCapacity}: da verificare sul dispositivo.");

        foreach (var r in p.Rules.Where(r => r.Func is JmFunc.AutoLeftRightOn or JmFunc.AutoLeftRightOff))
            Add(Severity.Warning, "AUTOLR_UNSUPPORTED", $"{Names.Rule(r)}: AUTO L/R non è gestito dal motore dei profili salvati.");
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Hat && r.Value > 0x0F))
            Add(Severity.Error, "HAT_RANGE", $"{Names.Rule(r)}: l'op HAT confronta solo 4 bit, il valore {r.Value} non può mai scattare.");
        foreach (var r in p.Rules.Where(r => (int)r.Func > 15 || (int)r.Op > 2))
            Add(Severity.Error, "BAD_ENUM", $"Regola con codici non validi (op {(int)r.Op}, f {(int)r.Func}).");

        // --- Conflicts (P6) -------------------------------------------------------------------
        foreach (var g in p.Rules.GroupBy(r => (r.Index, r.Value, r.Op)).Where(g => g.Select(r => r.Func).Distinct().Count() > 1))
        {
            var funcs = string.Join(" + ", g.Select(r => Names.Func(r.Func)).Distinct());
            Add(Severity.Warning, "SAME_CONDITION",
                $"Byte[{g.Key.Index}] {Names.Op(g.Key.Op)} {g.Key.Value}: la stessa condizione attiva {funcs} insieme. " +
                "Probabile tasto registrato due volte o D-pad catturato come asse.");
        }
        var bitRules = p.Rules.Where(r => r.Op == JmOp.BitAny).ToList();
        for (int a = 0; a < bitRules.Count; a++)
            for (int b = a + 1; b < bitRules.Count; b++)
            {
                var x = bitRules[a]; var y = bitRules[b];
                if (x.Index == y.Index && x.Value != y.Value && (x.Value & y.Value) != 0 && x.Func != y.Func)
                    Add(Severity.Warning, "OVERLAPPING_BITS",
                        $"{Names.Rule(x)} e {Names.Rule(y)} condividono dei bit: si attivano insieme.");
            }

        // --- Suspicious captures (P4, P5) ---------------------------------------------------------
        var byteHasBits = p.Rules.Where(r => r.Op == JmOp.BitAny).Select(r => r.Index).ToHashSet();
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Eq && FirmwareLimits.IsDirection(r.Func) && r.Value == 255))
            Add(Severity.Warning, "AXIS_CAPTURED_AS_VALUE",
                $"{Names.Rule(r)}: direzione catturata come asse (valore 255, \"Force Axis\"). Scatta solo a fondo corsa in un verso.");
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Eq && r.Value != 0 && (r.Value & (r.Value - 1)) == 0
                                             && (byteHasBits.Contains(r.Index) || p.Rules.Count(o => o.Index == r.Index) > 1)))
            Add(Severity.Warning, "EQ_ON_BUTTON_BYTE",
                $"{Names.Rule(r)}: confronto esatto (==) su un byte di pulsanti: scatta solo se quel tasto è l'unico premuto. Meglio BIT.");
        // HAT compares only the low nibble: buttons in the high nibble (bits 4-7) do not disturb it.
        var lowNibbleButtons = p.Rules.Where(r => r.Op == JmOp.BitAny && (r.Value & 0x0F) != 0).Select(r => r.Index).ToHashSet();
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Hat && lowNibbleButtons.Contains(r.Index)))
            Add(Severity.Warning, "HAT_ON_BUTTON_BYTE",
                $"{Names.Rule(r)}: op HAT su un byte che contiene pulsanti: scatta solo se i 4 bit bassi valgono esattamente {r.Value}, cioè con nessun altro tasto premuto.");

        // --- Axes ---------------------------------------------------------------------------------
        foreach (var idx in p.AnalogX.Intersect(p.AnalogY))
            Add(Severity.Warning, "SAME_AXIS_XY", $"Il byte {idx} è usato sia come asse X sia come asse Y: il movimento sarebbe solo in diagonale.");
        foreach (var idx in p.AnalogX.Concat(p.AnalogY).Distinct().Where(i => p.Rules.Any(r => r.Index == i)))
            Add(Severity.Warning, "AXIS_ON_RULE_BYTE", $"Il byte {idx} è usato sia come asse sia nelle regole: probabilmente non è un asse.");

        // --- XInput (P8) ------------------------------------------------------------------------
        if (p.Vid == 0x045E && p.Pid == 0x02FF)
            Add(Severity.Error, "VIRTUAL_XBOX_ID",
                "VID/PID 045E:02FF è il controller virtuale che Windows crea per i pad Xbox One/Series: l'adattatore vede " +
                "il VID/PID USB reale (es. 045E:02EA), quindi questo profilo non si attiverebbe mai.");
        else if (XInputLayout.IsKnownXboxOne(p.Vid, p.Pid))
        {
            Add(Severity.Info, "GIP_FIRMWARE", $"Controller Xbox One/Series (GIP): funziona solo con il firmware {XInputLayout.GipMinFirmware} o successivo.");
            if (p.Rules.Any(r => r.Index < 4 || r.Index > 9) || p.UseAnalog)
                Add(Severity.Error, "GIP_SYNTHETIC_INDEXES",
                    "Pad Xbox One/Series: gli indici non corrispondono al report GIP nativo (pulsanti ai byte 4-5, grilletti 6-9). " +
                    "Probabilmente vengono dal report HID sintetico di Windows: queste regole non funzionano sull'adattatore.");
        }
        else if (XInputLayout.IsKnownWirelessReceiver(p.Vid, p.Pid))
            Add(Severity.Error, "XBOX_RECEIVER_UNSUPPORTED", "Ricevitore wireless Xbox 360: formato dei report diverso, non supportato.");
        else if (XInputLayout.IsKnownXbox360Wired(p.Vid, p.Pid))
        {
            bool synthetic = p.Rules.Any(r => r.Index >= 6) || p.UseAnalog;
            if (synthetic)
                Add(Severity.Error, "XINPUT_SYNTHETIC_INDEXES",
                    "Pad Xbox 360: gli indici sembrano presi dal report HID sintetico di Windows (pulsanti al byte 10, hat al byte 11). " +
                    "L'ESP32 riceve il report XInput nativo (D-pad byte 2, pulsanti byte 3): queste regole non funzionano sull'adattatore.");
        }

        // --- Compiled .h semantics (P7) -------------------------------------------------------------
        if (header is { HeaderUsesDpadIndex: true } && p.DpadIndex >= 0)
        {
            var ignored = p.Rules.Where(r => r.Index == p.DpadIndex && !FirmwareLimits.IsDirection(r.Func)).ToList();
            if (ignored.Count > 0)
                Add(Severity.Warning, "HEADER_DPAD_INDEX",
                    $"Nel .h compilato JM_DPAD_INDEX = {p.DpadIndex}: le regole non direzionali su quel byte vengono ignorate " +
                    $"({string.Join(", ", ignored.Select(r => Names.Func(r.Func)).Distinct())}). Con SAVE invece funzionano.");
            var lost = p.Rules.Where(r => r.Index != p.DpadIndex && FirmwareLimits.IsDirection(r.Func)).ToList();
            if (lost.Count > 0)
                Add(Severity.Warning, "HEADER_DIR_OUTSIDE_DPAD",
                    $"Nel .h compilato le direzioni fuori dal byte {p.DpadIndex} vengono ignorate ({string.Join(", ", lost.Select(Names.Rule))}).");
        }

        return issues;
    }
}
