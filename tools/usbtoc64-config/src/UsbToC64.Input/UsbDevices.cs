using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace UsbToC64.Input;

/// <summary>A USB device (or interface of a composite device) currently present, with the Windows driver bound to it.</summary>
public sealed record UsbDeviceInfo(ushort Vid, ushort Pid, string Service, string InstanceId);

/// <summary>
/// Present USB devices through SetupAPI. Needed for Xbox One/Series pads: Windows shows them as a virtual
/// HID device 045E:02FF created by the xboxgip driver, while the adapter sees the real USB VID/PID.
/// </summary>
public static class UsbDevices
{
    private static readonly Regex VidPid = new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);

    public static List<UsbDeviceInfo> Present()
    {
        var list = new List<UsbDeviceInfo>();
        if (!OperatingSystem.IsWindows()) return list;
        try { Enumerate(list); }
        catch (Exception) { /* SetupAPI not available: empty list, callers fall back to the known VID/PID */ }
        return list;
    }

    [SupportedOSPlatform("windows")]
    private static void Enumerate(List<UsbDeviceInfo> list)
    {
        IntPtr set = Native.SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return;
        try
        {
            var data = new Native.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<Native.SP_DEVINFO_DATA>() };
            var id = new StringBuilder(512);
            var buf = new byte[512];
            for (uint i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                id.Clear();
                if (!Native.SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out _)) continue;
                var instance = id.ToString();
                var m = VidPid.Match(instance);
                if (!m.Success) continue;
                string service = "";
                if (Native.SetupDiGetDeviceRegistryPropertyW(set, ref data, Native.SPDRP_SERVICE, out _, buf, buf.Length, out int req) && req > 2)
                    service = Encoding.Unicode.GetString(buf, 0, req).TrimEnd('\0');
                list.Add(new UsbDeviceInfo(
                    ushort.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    ushort.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    service, instance));
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static class Native
    {
        public const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4, SPDRP_SERVICE = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwnd, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property,
            out uint regType, byte[] buffer, int size, out int required);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
