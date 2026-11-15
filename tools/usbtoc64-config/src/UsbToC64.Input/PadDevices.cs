using HidSharp;
using UsbToC64.Core.Hid;
using UsbToC64.Core.XInput;

namespace UsbToC64.Input;

/// <summary>A game controller seen by the operating system.</summary>
public sealed class PadDevice
{
    internal HidDevice Hid { get; }
    public string Path => Hid.DevicePath;
    /// <summary>VID/PID the adapter will see (for Xbox One/Series: the real USB device, not the Windows virtual one).</summary>
    public ushort Vid { get; }
    public ushort Pid { get; }
    /// <summary>Set when Windows shows a different (virtual) VID/PID than the USB device.</summary>
    public string? IdNote { get; }
    public string Name { get; }
    /// <summary>XInput pad: Windows shows a synthetic HID report, the layout comes from <see cref="XInputLayout"/>.</summary>
    public bool IsXInput { get; }
    /// <summary>XInput protocol: Xbox 360 or Xbox One/Series (GIP).</summary>
    public XInputKind XInputKind { get; }
    public byte[]? RawDescriptor { get; }
    public string? DescriptorError { get; }

    internal PadDevice(HidDevice hid, string name, bool isXInput, XInputKind kind, ushort vid, ushort pid, string? idNote,
        byte[]? raw, string? error)
    {
        Hid = hid; Name = name; IsXInput = isXInput; XInputKind = kind; Vid = vid; Pid = pid; IdNote = idNote;
        RawDescriptor = raw; DescriptorError = error;
    }

    /// <summary>Controls of the pad, located as the ESP32 sees them.</summary>
    public PadDescription Describe()
    {
        if (IsXInput)
        {
            var x = XInputLayout.Describe(XInputKind);
            if (IdNote != null) x.Notes.Add(IdNote);
            return x;
        }
        if (RawDescriptor == null)
            throw new InvalidOperationException($"Descrittore HID non disponibile: {DescriptorError}");
        return ControlClassifier.Classify(RawDescriptor);
    }

    public override string ToString() =>
        $"{Vid:X4}:{Pid:X4} {Name}{(IsXInput ? XInputKind == XInputKind.Gip ? " [XInput, Xbox One/Series]" : " [XInput, Xbox 360]" : "")}";
}

public static class PadDevices
{
    private static readonly HashSet<ushort> GameUsages = new() { 0x04, 0x05, 0x08 }; // joystick, gamepad, multi-axis

    /// <summary>Lists HID game controllers (joystick / gamepad / multi-axis top-level collections).</summary>
    public static List<PadDevice> List()
    {
        var result = new List<PadDevice>();
        var usb = UsbDevices.Present();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            byte[]? raw = null; string? err = null;
            try { raw = d.GetRawReportDescriptor(); }
            catch (Exception ex) { err = ex.Message; }

            bool isGame;
            if (raw != null)
            {
                try
                {
                    var layout = HidDescriptorParser.Parse(raw);
                    isGame = layout.Elements.Any(e => GameUsages.Contains(e.ApplicationUsage));
                }
                catch { isGame = false; }
            }
            else isGame = false;

            bool xinput = XInputLayout.IsXInputDevicePath(d.DevicePath) || XInputLayout.IsKnownXInput((ushort)d.VendorID, (ushort)d.ProductID);
            if (!isGame && !xinput) continue;

            string name;
            try { name = d.GetProductName()?.Trim() ?? ""; } catch { name = ""; }
            if (name.Length == 0) name = "(senza nome)";
            ushort vid = (ushort)d.VendorID, pid = (ushort)d.ProductID;
            var kind = XInputKind.Xbox360;
            string? note = null;
            if (xinput) (kind, vid, pid, note) = ResolveXInput(vid, pid, usb);
            result.Add(new PadDevice(d, name, xinput, kind, vid, pid, note, raw, err));
        }
        // Windows can expose the same pad more than once (one entry per top-level collection).
        return result.GroupBy(p => p.Path).Select(g => g.First()).ToList();
    }

    /// <summary>Virtual HID device that Windows (xboxgip / Windows.Gaming.Input) creates for every GIP pad.</summary>
    private const ushort VirtualGipVid = 0x045E, VirtualGipPid = 0x02FF;

    /// <summary>
    /// Xbox 360 or Xbox One/Series, and the VID/PID the adapter will see.
    /// 1) The USB device with the same VID/PID is present: its Windows driver tells the protocol
    ///    (xboxgip = GIP, xusb2x = Xbox 360).
    /// 2) Otherwise (e.g. 045E:02FF, the virtual device of GIP pads) the present USB device bound to xboxgip
    ///    is the real controller: GIP, with its VID/PID.
    /// 3) Otherwise the known VID/PID list; default Xbox 360.
    /// </summary>
    public static (XInputKind Kind, ushort Vid, ushort Pid, string? Note) ResolveXInput(ushort vid, ushort pid, IReadOnlyList<UsbDeviceInfo> usb)
    {
        // Wired Xbox One/Series pads are bound to xboxgip (recent Windows) or dc1-controller ("Xbox Gaming Device");
        // as a fallback, a known Xbox One/Series VID/PID.
        static bool IsGip(UsbDeviceInfo u) =>
            u.Service.Equals("xboxgip", StringComparison.OrdinalIgnoreCase) ||
            u.Service.Equals("dc1-controller", StringComparison.OrdinalIgnoreCase) ||
            (XInputLayout.IsKnownXboxOne(u.Vid, u.Pid) && !(u.Vid == VirtualGipVid && u.Pid == VirtualGipPid));
        static bool Is360(UsbDeviceInfo u) => u.Service.StartsWith("xusb", StringComparison.OrdinalIgnoreCase);

        var same = usb.Where(u => u.Vid == vid && u.Pid == pid).ToList();
        if (same.Any(IsGip)) return (XInputKind.Gip, vid, pid, null);
        if (same.Any(Is360)) return (XInputKind.Xbox360, vid, pid, null);

        var gip = usb.Where(IsGip).Select(u => (u.Vid, u.Pid)).Distinct().ToList();
        bool virtualGip = vid == VirtualGipVid && pid == VirtualGipPid;
        if (gip.Count > 0 && (virtualGip || same.Count == 0))
        {
            var (rv, rp) = gip[0];
            string note = $"Windows mostra il controller come {vid:X4}:{pid:X4} (dispositivo virtuale del driver Xbox): " +
                          $"il profilo usa il VID/PID USB reale {rv:X4}:{rp:X4}, quello che vede l'adattatore.";
            if (gip.Count > 1)
                note += $" Attenzione: ci sono {gip.Count} controller Xbox One/Series collegati ({string.Join(", ", gip.Select(g => $"{g.Vid:X4}:{g.Pid:X4}"))}): " +
                        "lasciane collegato uno solo.";
            return (XInputKind.Gip, rv, rp, note);
        }
        if (virtualGip)
        {
            var ms = usb.Where(u => u.Vid == 0x045E).Select(u => $"{u.Vid:X4}:{u.Pid:X4} (driver \"{u.Service}\")").Distinct().ToList();
            return (XInputKind.Gip, vid, pid,
                "Controller Xbox One/Series, ma il dispositivo USB reale non è stato trovato: il VID/PID del profilo (045E:02FF) " +
                "non corrisponderà a quello che vede l'adattatore. Dispositivi USB Microsoft collegati: " +
                (ms.Count == 0 ? "nessuno" : string.Join(", ", ms)) + ".");
        }
        return (XInputLayout.IsKnownXboxOne(vid, pid) ? XInputKind.Gip : XInputKind.Xbox360, vid, pid, null);
    }
}
