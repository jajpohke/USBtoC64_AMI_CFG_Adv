namespace UsbToC64.Core.Device;

/// <summary>Phase of a long adapter operation (firmware installation, profile save).</summary>
public enum OpPhase
{
    Connect,   // opening the port / esptool connecting to the chip
    Erase,     // esptool erases the whole flash (no percentage)
    Write,     // esptool writes the image (percentage)
    Verify,    // esptool checks the written data
    Restart,   // the adapter restarts
    Check,     // first contact after the restart
    Send,      // the profile is sent
    ReadBack,  // the profile is read back
    Compare,   // sent vs stored
    Done,
}

/// <param name="Overall">0..1 for the whole operation; null = this phase has no measurable progress.</param>
/// <param name="Percent">Percentage of the phase when known (esptool writing).</param>
public sealed record OpProgress(OpPhase Phase, double? Overall, int? Percent = null);
