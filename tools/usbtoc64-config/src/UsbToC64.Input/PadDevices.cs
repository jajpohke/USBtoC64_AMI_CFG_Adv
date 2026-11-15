using HidSharp;
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
    public byte[]? RawDescriptor { get; }
    public string? DescriptorError { get; }

    internal PadDevice(HidDevice hid, string name, bool isXInput, byte[]? raw, string? error)
    {
        Hid = hid; Name = name; IsXInput = isXInput; RawDescriptor = raw; DescriptorError = error;
    }

    /// <summary>Controls of the pad, located as the ESP32 sees them.</summary>
    public PadDescription Describe()
    {
        if (IsXInput) return XInputLayout.Describe();
        if (RawDescriptor == null)
            throw new InvalidOperationException($"Descrittore HID non disponibile: {DescriptorError}");
        return ControlClassifier.Classify(RawDescriptor);
    }

    public override string ToString() => $"{Vid:X4}:{Pid:X4} {Name}{(IsXInput ? " [XInput]" : "")}";
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
            try { name = d.GetProductName(); } catch { name = "(senza nome)"; }
            result.Add(new PadDevice(d, name, xinput, raw, err));
        }
        // Windows can expose the same pad more than once (one entry per top-level collection).
        return result.GroupBy(p => p.Path).Select(g => g.First()).ToList();
    }
}
