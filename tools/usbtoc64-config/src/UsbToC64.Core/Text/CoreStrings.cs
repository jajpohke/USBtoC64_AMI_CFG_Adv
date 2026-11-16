namespace UsbToC64.Core.Text;

/// <summary>Texts of UsbToC64.Core, UsbToC64.Input and UsbToC64.Device (messages shown by the App and the CLI).</summary>
public static partial class CoreStrings
{
    public static IEnumerable<Tr> All => Common;

    private static readonly Tr[] Common =
    {
        new("common.c64amiga", "C64/Amiga on DB9 port", "C64/Amiga su porta DB9", "C64/Amiga en puerto DB9", "C64/Amiga am DB9-Port"),
    };
}
