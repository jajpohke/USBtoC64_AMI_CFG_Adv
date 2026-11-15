namespace UsbToC64.Core.Hid;

/// <summary>Conversion between the report returned by Windows and the report the ESP32 receives.</summary>
public static class ReportView
{
    /// <summary>
    /// Windows always prepends the report id (0 when the device does not use ids);
    /// the ESP32 receives the id byte only when the device uses report ids.
    /// </summary>
    public static byte[] FromWindows(ReadOnlySpan<byte> windowsReport, bool usesReportIds)
    {
        if (windowsReport.IsEmpty) return Array.Empty<byte>();
        return usesReportIds ? windowsReport.ToArray() : windowsReport[1..].ToArray();
    }
}
