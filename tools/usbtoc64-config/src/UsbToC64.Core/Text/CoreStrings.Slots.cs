namespace UsbToC64.Core.Text;

public static partial class CoreStrings
{
    /// <summary>Profile slots (firmware 1.3.0).</summary>
    private static readonly Tr[] Slots =
    {
        new("slot.noreply", "The adapter did not answer the slot command.", "L'adattatore non ha risposto al comando dei cassetti.",
            "El adaptador no ha respondido al comando de las ranuras.", "Der Adapter hat auf den Speicherplatz-Befehl nicht geantwortet."),
        new("slot.accepted", "Profile accepted by the adapter (slot {0}).", "Profilo accettato dall'adattatore (cassetto {0}).",
            "Perfil aceptado por el adaptador (ranura {0}).", "Profil vom Adapter angenommen (Speicherplatz {0})."),
        new("slot.labeldiff", "Label: sent «{0}», stored «{1}»", "Etichetta: inviata «{0}», salvata «{1}»",
            "Etiqueta: enviada «{0}», guardada «{1}»", "Bezeichnung: gesendet «{0}», gespeichert «{1}»"),
        new("slot.verified", "Saved and verified ({0}): slot {1} «{2}».", "Salvato e verificato ({0}): cassetto {1} «{2}».",
            "Guardado y verificado ({0}): ranura {1} «{2}».", "Gespeichert und geprüft ({0}): Speicherplatz {1} «{2}»."),
        new("offline.button", "Byte {0} · bit 0x{1:X2}", "Byte {0} · bit 0x{1:X2}", "Byte {0} · bit 0x{1:X2}", "Byte {0} · Bit 0x{1:X2}"),
        new("offline.hat", "Byte {0} · directional pad", "Byte {0} · croce direzionale", "Byte {0} · cruceta", "Byte {0} · Steuerkreuz"),
        new("offline.axis", "Byte {0} · axis", "Byte {0} · asse", "Byte {0} · eje", "Byte {0} · Achse"),
    };
}
