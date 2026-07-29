using RetroTools.Core.Platforms;

namespace RetroTools.Core.Palettes;

/// <summary>
/// Αρχικές αντιστοιχίσεις slot → χρώμα υλικού για κάθε mode.
/// Ο χρήστης τις αλλάζει ελεύθερα· εδώ απλώς επιλέγονται λογικά σημεία εκκίνησης
/// ώστε ένα νέο sprite να μη ξεκινά με δεκαέξι ίδια μαύρα pens.
/// </summary>
public static class DefaultPalettes
{
    /// <summary>
    /// Πρώτα οι έντονες αποχρώσεις και μετά οι μισής έντασης: έτσι τα πρώτα pens
    /// που θα πιάσει ο χρήστης ξεχωρίζουν μεταξύ τους.
    /// </summary>
    private static readonly int[] CpcPens =
    {
        0,  // Black
        26, // Bright White
        6,  // Bright Red
        24, // Bright Yellow
        18, // Bright Green
        20, // Bright Cyan
        2,  // Bright Blue
        8,  // Bright Magenta
        13, // White (μισή ένταση)
        3,  // Red
        9,  // Green
        1,  // Blue
        15, // Orange
        10, // Cyan
        4,  // Magenta
        12, // Yellow
    };

    private static readonly int[] C64Slots = { 0, 1, 11, 12, 15, 5, 14, 7 };

    public static IReadOnlyList<int> For(GraphicsMode mode)
    {
        if (mode == null)
        {
            throw new ArgumentNullException(nameof(mode));
        }

        var slotCount = mode.MaxPixelValue + 1;
        var result = new int[slotCount];

        switch (mode.Platform)
        {
            case PlatformId.AmstradCpc:
                for (var i = 0; i < slotCount; i++)
                {
                    result[i] = CpcPens[i % CpcPens.Length];
                }

                break;

            case PlatformId.Commodore64:
                for (var i = 0; i < slotCount; i++)
                {
                    result[i] = C64Slots[i % C64Slots.Length];
                }

                break;

            case PlatformId.ZxSpectrum:
                // Slot 0 = PAPER (μαύρο), slot 1 = INK (φωτεινό λευκό).
                result[0] = 0;

                if (slotCount > 1)
                {
                    result[1] = 15;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode.Platform, "Άγνωστη πλατφόρμα.");
        }

        return result;
    }
}
