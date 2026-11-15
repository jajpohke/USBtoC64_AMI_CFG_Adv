using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UsbToC64.Core.Device;

namespace UsbToC64.Device;

/// <summary>
/// Serial link opened directly with the Win32 API, the way the Arduino IDE serial monitor (go.bug.st/serial) does:
/// one SetCommState that sets baud rate and DTR/RTS together, starting from the current state.
/// System.IO.Ports first clears DTR/RTS and then sets them one by one: on the ESP32-S3 native USB port that
/// passes through "RTS on, DTR off", which is the chip-reset command (rst:0x15 USB_UART_CHIP_RESET).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32SerialLink : IAdapterLink
{
    private readonly SafeFileHandle _h;
    private readonly string _port;
    private readonly string _lines;
    private readonly StringBuilder _buf = new();
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
    private readonly byte[] _rx = new byte[1024];

    private const int ChunkSize = 32;
    private const int ChunkDelayMs = 15;

    public string Name => $"{_port} ({_lines})";
    public bool Lost { get; private set; }

    private Win32SerialLink(SafeFileHandle h, string port, string lines)
    {
        _h = h; _port = port; _lines = lines;
    }

    public static Win32SerialLink Open(string port, bool dtr, bool rts)
    {
        var h = Native.CreateFileW(@"\\.\" + port, Native.GENERIC_READ | Native.GENERIC_WRITE, 0, IntPtr.Zero,
                                   Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            throw err == 5 // ERROR_ACCESS_DENIED
                ? new UnauthorizedAccessException($"La porta {port} è occupata da un altro programma (IDE di Arduino, terminale, browser con il configuratore web…).")
                : new IOException($"Impossibile aprire {port}: {new Win32Exception(err).Message}");
        }
        try
        {
            var dcb = new Native.DCB { DCBlength = (uint)Marshal.SizeOf<Native.DCB>() };
            if (!Native.GetCommState(h, ref dcb)) throw LastError("GetCommState", port);
            dcb.BaudRate = 115200;
            dcb.ByteSize = 8;
            dcb.Parity = 0;   // NOPARITY
            dcb.StopBits = 0; // ONESTOPBIT
            // fBinary (bit 0), fDtrControl (bits 4-5), fRtsControl (bits 12-13); no flow control.
            dcb.Flags = 0x0001 | ((dtr ? 1u : 0u) << 4) | ((rts ? 1u : 0u) << 12);
            if (!Native.SetCommState(h, ref dcb)) throw LastError("SetCommState", port);

            // Non-blocking reads (return immediately with what is there), writes time out after 2 s.
            var t = new Native.COMMTIMEOUTS
            {
                ReadIntervalTimeout = uint.MaxValue, ReadTotalTimeoutMultiplier = 0, ReadTotalTimeoutConstant = 0,
                WriteTotalTimeoutMultiplier = 0, WriteTotalTimeoutConstant = 2000,
            };
            if (!Native.SetCommTimeouts(h, ref t)) throw LastError("SetCommTimeouts", port);
            return new Win32SerialLink(h, port, $"DTR={(dtr ? 1 : 0)} RTS={(rts ? 1 : 0)}");
        }
        catch
        {
            h.Dispose();
            throw;
        }
    }

    private static IOException LastError(string what, string port) =>
        new($"{what} su {port}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

    public void SendLine(string line)
    {
        // Paced chunks: the original firmware has a 256-byte USB RX buffer read only from loop().
        var data = Encoding.UTF8.GetBytes(line + "\n");
        for (int i = 0; i < data.Length; i += ChunkSize)
        {
            int n = Math.Min(ChunkSize, data.Length - i);
            var chunk = new byte[n];
            Array.Copy(data, i, chunk, 0, n);
            if (!Native.WriteFile(_h, chunk, n, out int written, IntPtr.Zero) || written != n)
            {
                Lost = true;
                throw new IOException($"Invio non riuscito su {_port}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }
            if (i + ChunkSize < data.Length) Thread.Sleep(ChunkDelayMs);
        }
    }

    public string? ReadLine(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var s = _buf.ToString();
            int nl = s.IndexOf('\n');
            if (nl >= 0)
            {
                _buf.Remove(0, nl + 1);
                return s[..nl].TrimEnd('\r');
            }
            if (Lost || DateTime.UtcNow >= until) return null;
            if (!Native.ReadFile(_h, _rx, _rx.Length, out int read, IntPtr.Zero))
            {
                // The device went away (reboot / USB reconnection): "device not functioning".
                Lost = true;
                return null;
            }
            if (read > 0)
            {
                var chars = new char[_utf8.GetCharCount(_rx, 0, read)];
                _utf8.GetChars(_rx, 0, read, chars, 0);
                _buf.Append(chars);
            }
            else Thread.Sleep(10);
        }
    }

    public void DiscardInput()
    {
        if (!Native.PurgeComm(_h, Native.PURGE_RXABORT | Native.PURGE_RXCLEAR)) Lost = true;
        _buf.Clear();
    }

    public void Dispose() => _h.Dispose();

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, OPEN_EXISTING = 3;
        public const uint PURGE_RXABORT = 0x0002, PURGE_RXCLEAR = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        public struct DCB
        {
            public uint DCBlength;
            public uint BaudRate;
            public uint Flags;
            public ushort wReserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public byte XonChar;
            public byte XoffChar;
            public byte ErrorChar;
            public byte EofChar;
            public byte EvtChar;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetCommState(SafeFileHandle h, ref DCB dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetCommState(SafeFileHandle h, ref DCB dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetCommTimeouts(SafeFileHandle h, ref COMMTIMEOUTS t);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int toWrite, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool PurgeComm(SafeFileHandle h, uint flags);
    }
}
