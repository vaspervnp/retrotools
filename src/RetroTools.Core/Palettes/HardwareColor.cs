namespace RetroTools.Core.Palettes;

/// <summary>
/// Ένα χρώμα της παλέτας του υλικού. Το <see cref="Index"/> είναι ό,τι αποθηκεύεται
/// στη βάση — τα RGB προκύπτουν από το επιλεγμένο <see cref="PaletteProfile"/>.
/// </summary>
/// <param name="Index">
/// Ο αριθμός του χρώματος όπως τον ξέρει το σύστημα:
/// CPC firmware colour 0–26, C64 colour 0–15, ZX ink 0–7 + 8·BRIGHT.
/// </param>
/// <param name="Name">Καθιερωμένο όνομα (π.χ. "Pastel Yellow", "Light Red").</param>
/// <param name="HardwareValues">
/// Οι τιμές που γράφονται πραγματικά στο υλικό. Στον CPC είναι τα Gate Array inks
/// 0x40–0x5F (κάποια firmware χρώματα έχουν δύο). Αλλού είναι ίδιο με το Index.
/// </param>
public sealed record HardwareColor(int Index, string Name, IReadOnlyList<byte> HardwareValues)
{
    /// <summary>
    /// Η κανονική τιμή που στέλνουμε στο υλικό όταν υπάρχουν περισσότερες από μία.
    /// </summary>
    public byte PrimaryHardwareValue
    {
        get { return HardwareValues.Count > 0 ? HardwareValues[0] : (byte)Index; }
    }

    public static HardwareColor Simple(int index, string name)
    {
        return new HardwareColor(index, name, new[] { (byte)index });
    }
}
