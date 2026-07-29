namespace RetroTools.Core.Platforms;

/// <summary>
/// Ένα γραφικό mode μιας πλατφόρμας, με όλα όσα χρειάζεται ο editor για να
/// επιβάλλει τους σωστούς περιορισμούς και να δείχνει σωστό preview.
/// </summary>
/// <param name="Code">Σταθερό αναγνωριστικό που αποθηκεύεται στη βάση (π.χ. "cpc.mode0").</param>
/// <param name="BitsPerPixel">Bits ανά pixel στην packed μορφή του υλικού.</param>
/// <param name="PaletteSlots">Πόσα slots (pens / colour registers) έχει η παλέτα σε αυτό το mode.</param>
/// <param name="MaxColorsPerCell">
/// Πόσα διαφορετικά χρώματα μπορούν να συνυπάρξουν στην περιοχή που ορίζει το
/// <paramref name="ColorScope"/>. Ισούται με <paramref name="PaletteSlots"/> όταν το χρώμα είναι per-pixel.
/// </param>
/// <param name="CellWidth">Πλάτος περιοχής χρώματος σε pixels (8 για ZX/C64 cells, 0 αν δεν ισχύει).</param>
public sealed record GraphicsMode(
    string Code,
    string Name,
    PlatformId Platform,
    int ScreenWidth,
    int ScreenHeight,
    int BitsPerPixel,
    int PaletteSlots,
    int MaxColorsPerCell,
    ColorScope ColorScope,
    int CellWidth,
    int CellHeight,
    PixelAspect PixelAspect,
    SpriteSizeRule SpriteSize,
    bool IsHardwareSprite,
    bool SupportsMask,
    string Notes)
{
    private readonly IReadOnlyList<PixelSlot>? _pixelSlots;

    /// <summary>
    /// Τι σημαίνει κάθε τιμή pixel σε αυτό το mode. Το πλήθος ισούται πάντα με
    /// <see cref="MaxPixelValue"/> + 1. Αν δεν δηλωθεί ρητά, θεωρούνται ελεύθερα pens.
    /// </summary>
    public IReadOnlyList<PixelSlot> PixelSlots
    {
        get
        {
            if (_pixelSlots != null)
            {
                return _pixelSlots;
            }

            var slots = new PixelSlot[MaxPixelValue + 1];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = PixelSlot.Free(i);
            }

            return slots;
        }

        init { _pixelSlots = value; }
    }

    /// <summary>Τα slots που μοιράζονται με άλλα sprites/κελιά — αλλαγή τους είναι καθολική.</summary>
    public IReadOnlyList<PixelSlot> SharedSlots
    {
        get { return PixelSlots.Where(s => s.IsGlobal).ToList(); }
    }

    public bool HasTransparentSlot
    {
        get { return PixelSlots.Any(s => s.Role == PixelSlotRole.Transparent); }
    }

    /// <summary>Πόσα pixels χωράνε σε ένα byte της packed μορφής.</summary>
    public int PixelsPerByte
    {
        get { return 8 / BitsPerPixel; }
    }

    /// <summary>Bytes ανά γραμμή για δεδομένο πλάτος σε pixels.</summary>
    public int BytesPerRow(int widthInPixels)
    {
        return (widthInPixels + PixelsPerByte - 1) / PixelsPerByte;
    }

    /// <summary>Συνολικά bytes για ένα sprite των δεδομένων διαστάσεων (χωρίς mask).</summary>
    public int PackedSize(int widthInPixels, int heightInPixels)
    {
        return BytesPerRow(widthInPixels) * heightInPixels;
    }

    /// <summary>Πόσες διαφορετικές τιμές χωράει ένα pixel στην packed μορφή (2^bpp).</summary>
    public int PixelValueCount
    {
        get { return 1 << BitsPerPixel; }
    }

    /// <summary>
    /// Η μέγιστη έγκυρη τιμή ενός pixel στο indexed buffer.
    /// Σε per-cell modes ο δείκτης δείχνει στα χρώματα <b>του κελιού</b> (π.χ. 0 = PAPER, 1 = INK
    /// στο Spectrum), όχι στην παλέτα — γι' αυτό το όριο διαφέρει.
    /// </summary>
    public int MaxPixelValue
    {
        get { return ColorScope == ColorScope.PerCell ? PixelValueCount - 1 : PaletteSlots - 1; }
    }
}
