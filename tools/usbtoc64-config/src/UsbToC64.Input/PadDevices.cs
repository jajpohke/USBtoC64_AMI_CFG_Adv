using HidSharp;
using Microsoft.Win32;
using UsbToC64.Core.Hid;
using UsbToC64.Core.XInput;

namespace UsbToC64.Input;

/// <summary>A game controller seen by the operating system.</summary>
public sealed class PadDevice
{
    internal HidDevice Hid { get; }
    public string Path => Hid.DevicePath;
    public ushort Vid => (ushort)Hid.VendorID;
    public ushort Pid => (ushort)Hid.ProductID;
    public string Name { get; }
    /// <summary>XInput pad: Windows shows a synthetic HID report, the layout comes from <see cref="XInputLayout"/>.</summary>
    public bool IsXInput { get; }
    /// <summary>XInput protocol: Xbox 360 or Xbox One/Series (GIP).</summary>
    public XInputKind XInputKind { get; }
    public byte[]? RawDescriptor { get; }
    public string? DescriptorError { get; }

    internal PadDevice(HidDevice hid, string name, bool isXInput, XInputKind kind, byte[]? raw, string? error)
    {
        Hid = hid; Name = name; IsXInput = isXInput; XInputKind = kind; RawDescriptor = raw; DescriptorError = error;
    }

    /// <summary>Controls of the pad, located as the ESP32 sees them.</summary>
    public PadDescription Describe()
    {
        if (IsXInput) return XInputLayout.Describe(XInputKind);
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
            var kind = xinput ? ResolveXInputKind((ushort)d.VendorID, (ushort)d.ProductID) : XInputKind.Xbox360;
            result.Add(new PadDevice(d, name, xinput, kind, raw, err));
        }
        // Windows can expose the same pad more than once (one entry per top-level collection).
        return result.GroupBy(p => p.Path).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// Xbox 360 or Xbox One/Series: the Windows driver bound to the USB device tells it for sure
    /// (xboxgip = GIP, xusb2x = Xbox 360); otherwise the list of known VID/PID; default Xbox 360.
    /// </summary>
    public static XInputKind ResolveXInputKind(ushort vid, ushort pid)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                string prefix = $"VID_{vid:X4}&PID_{pid:X4}";
                foreach (var dev in usb?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    if (!dev.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var devKey = usb!.OpenSubKey(dev);
                    foreach (var inst in devKey?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var instKey = devKey!.OpenSubKey(inst);
                        var service = instKey?.GetValue("Service") as string ?? "";
                        if (service.Equals("xboxgip", StringComparison.OrdinalIgnoreCase)) return XInputKind.Gip;
                        if (service.StartsWith("xusb", StringComparison.OrdinalIgnoreCase)) return XInputKind.Xbox360;
                    }
                }
            }
            catch (Exception) { /* registry not readable: fall back to the known list */ }
        }
        return XInputLayout.IsKnownXboxOne(vid, pid) ? XInputKind.Gip : XInputKind.Xbox360;
    }
}
