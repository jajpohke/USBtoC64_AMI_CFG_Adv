using System.Runtime.InteropServices;
using HidSharp;
using UsbToC64.Core.Hid;
using UsbToC64.Core.XInput;
using UsbToC64.Core.Text;

namespace UsbToC64.Input;

/// <summary>
/// Reads the pad and returns reports in the ESP32 view (same byte indexes the firmware rules use).
/// HID pads: raw input reports. XInput pads: native report rebuilt from the XInput state.
/// </summary>
public sealed class PadReader : IDisposable
{
    private readonly HidStream? _stream;
    private readonly byte[] _buffer = Array.Empty<byte>();
    private readonly bool _usesReportIds;
    private readonly int _xinputSlot = -1;
    private readonly XInputKind _kind;
    private uint _lastPacket = uint.MaxValue;

    public PadReader(PadDevice device, PadDescription description, int xinputSlot = 0)
    {
        if (device.IsXInput)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(L.T("input.xinputwindows"));
            _xinputSlot = xinputSlot;
            _kind = device.XInputKind;
            return;
        }
        _usesReportIds = description.UsesReportIds;
        var cfg = new OpenConfiguration();
        cfg.SetOption(OpenOption.Exclusive, false);
        _stream = device.Hid.Open(cfg);
        _stream.ReadTimeout = 100;
        _buffer = new byte[Math.Max(device.Hid.GetMaxInputReportLength(), 2)];
    }

    /// <summary>Next report, or null if nothing changed within the timeout.</summary>
    public byte[]? Read()
    {
        if (_stream != null)
        {
            try
            {
                int n = _stream.Read(_buffer, 0, _buffer.Length);
                return n <= 0 ? null : ReportView.FromWindows(_buffer.AsSpan(0, n), _usesReportIds);
            }
            catch (TimeoutException) { return null; }
        }

        Thread.Sleep(10);
        if (XInputNative.XInputGetState((uint)_xinputSlot, out var st) != 0) return null; // not connected
        if (st.dwPacketNumber == _lastPacket) return null;
        _lastPacket = st.dwPacketNumber;
        var g = st.Gamepad;
        return _kind == XInputKind.Gip
            ? XInputLayout.BuildGipReport(g.wButtons, g.bLeftTrigger, g.bRightTrigger, g.sThumbLX, g.sThumbLY, g.sThumbRX, g.sThumbRY)
            : XInputLayout.BuildNativeReport(g.wButtons, g.bLeftTrigger, g.bRightTrigger, g.sThumbLX, g.sThumbLY, g.sThumbRX, g.sThumbRY);
    }

    /// <summary>XInput slots (0-3) with a connected controller.</summary>
    public static List<int> ConnectedXInputSlots()
    {
        var list = new List<int>();
        if (!OperatingSystem.IsWindows()) return list;
        for (uint i = 0; i < 4; i++)
        {
            try { if (XInputNative.XInputGetState(i, out _) == 0) list.Add((int)i); }
            catch (DllNotFoundException) { break; }
        }
        return list;
    }

    public void Dispose() => _stream?.Dispose();
}

internal static class XInputNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    internal static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
}
