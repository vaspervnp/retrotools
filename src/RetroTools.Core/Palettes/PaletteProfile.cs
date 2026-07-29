namespace RetroTools.Core.Palettes;

/// <summary>
/// Μια εκδοχή των RGB τιμών μιας παλέτας υλικού. Καθαρά θέμα <b>προβολής</b>:
/// τα δεδομένα των sprites αποθηκεύονται πάντα ως δείκτες, ποτέ ως RGB.
/// Υπάρχουν πολλαπλά profiles γιατί οι emulators διαφωνούν μεταξύ τους
/// (και με το πραγματικό υλικό) για το πώς ακριβώς φαίνεται κάθε χρώμα.
/// </summary>
public sealed record PaletteProfile(string Id, string Name, string Description, IReadOnlyList<Rgb24> Colors)
{
    public Rgb24 this[int colorIndex]
    {
        get
        {
            if (colorIndex < 0 || colorIndex >= Colors.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colorIndex),
                    colorIndex,
                    "Το profile '" + Id + "' έχει " + Colors.Count + " χρώματα.");
            }

            return Colors[colorIndex];
        }
    }
}
