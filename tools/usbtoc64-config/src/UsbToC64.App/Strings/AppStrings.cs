using UsbToC64.Core.Text;

namespace UsbToC64.App;

/// <summary>
/// Texts of the graphical program. One file per area (AppStrings.*.cs); this file only joins them.
/// The files are plain C# (no WPF) so that the tests can check them.
/// </summary>
public static partial class AppStrings
{
    public static IEnumerable<Tr> All => Main.Concat(Gamepad).Concat(Pages).Concat(Misc);
}
