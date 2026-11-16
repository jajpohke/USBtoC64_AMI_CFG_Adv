using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using UsbToC64.Core.XInput;
using UsbToC64.Core.Text;

namespace UsbToC64.Core.Validation;

public enum Severity { Info, Warning, Error }

public sealed record Issue(Severity Severity, string Code, string Message)
{
    public override string ToString() => $"[{L.T(Severity switch { Severity.Error => "val.sev.error", Severity.Warning => "val.sev.warning", _ => "val.sev.info" })}] {Code}: {Message}";
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
            Add(Severity.Error, "EMPTY", L.T("val.empty"));
        if (p.Vid == 0 || p.Pid == 0)
            Add(Severity.Error, "NO_VIDPID", L.F("val.novidpid", p.Vid, p.Pid));
        if (p.Rules.Count > FirmwareLimits.MaxRules)
            Add(Severity.Error, "TOO_MANY_RULES", L.F("val.toomanyrules", p.Rules.Count, FirmwareLimits.MaxRules));
        if (p.AnalogX.Count > FirmwareLimits.MaxAnalogPerAxis || p.AnalogY.Count > FirmwareLimits.MaxAnalogPerAxis)
            Add(Severity.Error, "TOO_MANY_AXES", L.F("val.toomanyaxes", FirmwareLimits.MaxAnalogPerAxis));

        int est = SaveJmJson.EstimateArduinoJsonBytes(p);
        if (est > FirmwareLimits.JsonDocumentCapacity)
            Add(Severity.Error, "JSON_TOO_BIG", L.F("val.jsontoobig", est, FirmwareLimits.JsonDocumentCapacity));
        else if (est > FirmwareLimits.JsonDocumentCapacity * 9 / 10)
            Add(Severity.Warning, "JSON_NEAR_LIMIT", L.F("val.jsonnearlimit", est, FirmwareLimits.JsonDocumentCapacity));

        foreach (var r in p.Rules.Where(r => r.Func is JmFunc.AutoLeftRightOn or JmFunc.AutoLeftRightOff))
            Add(Severity.Warning, "AUTOLR_UNSUPPORTED", L.F("val.autolr", Names.Rule(r)));
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Hat && r.Value > 0x0F))
            Add(Severity.Error, "HAT_RANGE", L.F("val.hatrange", Names.Rule(r), r.Value));
        if (p.Rules.Any(r => FirmwareLimits.IsSigned(r.Op)))
            Add(Severity.Info, "SIGNED_OPS",
                L.F("val.signedops", FirmwareLimits.SignedOpsMinFirmware));
        foreach (var r in p.Rules.Where(r => (int)r.Func > 15 || (int)r.Op > 4))
            Add(Severity.Error, "BAD_ENUM", L.F("val.badenum", (int)r.Op, (int)r.Func));

        // --- Conflicts (P6) -------------------------------------------------------------------
        foreach (var g in p.Rules.GroupBy(r => (r.Index, r.Value, r.Op)).Where(g => g.Select(r => r.Func).Distinct().Count() > 1))
        {
            var funcs = string.Join(" + ", g.Select(r => Names.Func(r.Func)).Distinct());
            Add(Severity.Warning, "SAME_CONDITION",
                L.F("val.samecondition", g.Key.Index, Names.Op(g.Key.Op), g.Key.Value, funcs));
        }
        var bitRules = p.Rules.Where(r => r.Op == JmOp.BitAny).ToList();
        for (int a = 0; a < bitRules.Count; a++)
            for (int b = a + 1; b < bitRules.Count; b++)
            {
                var x = bitRules[a]; var y = bitRules[b];
                if (x.Index == y.Index && x.Value != y.Value && (x.Value & y.Value) != 0 && x.Func != y.Func)
                    Add(Severity.Warning, "OVERLAPPING_BITS",
                        L.F("val.overlap", Names.Rule(x), Names.Rule(y)));
            }

        // --- Suspicious captures (P4, P5) ---------------------------------------------------------
        var byteHasBits = p.Rules.Where(r => r.Op == JmOp.BitAny).Select(r => r.Index).ToHashSet();
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Eq && FirmwareLimits.IsDirection(r.Func) && r.Value == 255))
            Add(Severity.Warning, "AXIS_CAPTURED_AS_VALUE",
                L.F("val.axisasvalue", Names.Rule(r)));
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Eq && r.Value != 0 && (r.Value & (r.Value - 1)) == 0
                                             && (byteHasBits.Contains(r.Index) || p.Rules.Count(o => o.Index == r.Index) > 1)))
            Add(Severity.Warning, "EQ_ON_BUTTON_BYTE",
                L.F("val.eqbutton", Names.Rule(r)));
        // HAT compares only the low nibble: buttons in the high nibble (bits 4-7) do not disturb it.
        var lowNibbleButtons = p.Rules.Where(r => r.Op == JmOp.BitAny && (r.Value & 0x0F) != 0).Select(r => r.Index).ToHashSet();
        foreach (var r in p.Rules.Where(r => r.Op == JmOp.Hat && lowNibbleButtons.Contains(r.Index)))
            Add(Severity.Warning, "HAT_ON_BUTTON_BYTE",
                L.F("val.hatbutton", Names.Rule(r), r.Value));

        // --- Axes ---------------------------------------------------------------------------------
        foreach (var idx in p.AnalogX.Intersect(p.AnalogY))
            Add(Severity.Warning, "SAME_AXIS_XY", L.F("val.sameaxisxy", idx));
        foreach (var idx in p.AnalogX.Concat(p.AnalogY).Distinct().Where(i => p.Rules.Any(r => r.Index == i)))
            Add(Severity.Warning, "AXIS_ON_RULE_BYTE", L.F("val.axisonrule", idx));

        // --- XInput (P8) ------------------------------------------------------------------------
        if (p.Vid == 0x045E && p.Pid == 0x02FF)
            Add(Severity.Error, "VIRTUAL_XBOX_ID",
                L.T("val.virtualxbox"));
        else if (XInputLayout.IsKnownXboxOne(p.Vid, p.Pid))
        {
            Add(Severity.Info, "GIP_FIRMWARE", L.F("val.gipfirmware", XInputLayout.GipMinFirmware));
            if (p.Rules.Any(r => r.Index < 4 || r.Index > 17 || (r.Index > 9 && !FirmwareLimits.IsSigned(r.Op))) || p.UseAnalog)
                Add(Severity.Error, "GIP_SYNTHETIC_INDEXES",
                    L.T("val.gipsynthetic"));
        }
        else if (XInputLayout.IsKnownWirelessReceiver(p.Vid, p.Pid))
            Add(Severity.Error, "XBOX_RECEIVER_UNSUPPORTED", L.T("val.receiver"));
        else if (XInputLayout.IsKnownXbox360Wired(p.Vid, p.Pid))
        {
            bool synthetic = p.Rules.Any(r => r.Index >= 14 || (r.Index >= 6 && !FirmwareLimits.IsSigned(r.Op))) || p.UseAnalog;
            if (synthetic)
                Add(Severity.Error, "XINPUT_SYNTHETIC_INDEXES",
                    L.T("val.xinputsynthetic"));
        }

        // --- Compiled .h semantics (P7) -------------------------------------------------------------
        if (header is { HeaderUsesDpadIndex: true } && p.DpadIndex >= 0)
        {
            var ignored = p.Rules.Where(r => r.Index == p.DpadIndex && !FirmwareLimits.IsDirection(r.Func)).ToList();
            if (ignored.Count > 0)
                Add(Severity.Warning, "HEADER_DPAD_INDEX",
                    L.F("val.headerdpad", p.DpadIndex, string.Join(", ", ignored.Select(r => Names.Func(r.Func)).Distinct())));
            var lost = p.Rules.Where(r => r.Index != p.DpadIndex && FirmwareLimits.IsDirection(r.Func)).ToList();
            if (lost.Count > 0)
                Add(Severity.Warning, "HEADER_DIR_OUTSIDE_DPAD",
                    L.F("val.headerdir", p.DpadIndex, string.Join(", ", lost.Select(Names.Rule))));
        }

        return issues;
    }
}
