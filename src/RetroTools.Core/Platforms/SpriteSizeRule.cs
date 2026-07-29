namespace RetroTools.Core.Platforms;

/// <summary>
/// Επιτρεπτές διαστάσεις sprite για ένα mode.
/// Το byte alignment δεν είναι αισθητική επιλογή: στον CPC Mode 1 τέσσερα pixels
/// μοιράζονται ένα byte, οπότε πλάτος 6 pixels δεν μπορεί να αποθηκευτεί καθαρά.
/// </summary>
public sealed record SpriteSizeRule(
    int WidthAlignment,
    int HeightAlignment,
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    int? FixedWidth,
    int? FixedHeight)
{
    /// <summary>Ελεύθερο μέγεθος με δεδομένο alignment (software sprites).</summary>
    public static SpriteSizeRule Aligned(int widthAlignment, int maxWidth = 128, int maxHeight = 128)
    {
        return new SpriteSizeRule(
            widthAlignment,
            1,
            widthAlignment,
            1,
            maxWidth,
            maxHeight,
            null,
            null);
    }

    /// <summary>Καρφωμένο μέγεθος (C64 hardware sprites, UDG χαρακτήρες).</summary>
    public static SpriteSizeRule Fixed(int width, int height)
    {
        return new SpriteSizeRule(width, height, width, height, width, height, width, height);
    }

    public bool IsFixed
    {
        get { return FixedWidth.HasValue && FixedHeight.HasValue; }
    }

    /// <summary>
    /// Ελέγχει διαστάσεις και επιστρέφει τους λόγους απόρριψης (κενή λίστα = έγκυρο).
    /// </summary>
    public IReadOnlyList<string> Validate(int width, int height)
    {
        var errors = new List<string>();

        if (IsFixed)
        {
            if (width != FixedWidth || height != FixedHeight)
            {
                errors.Add("Το mode απαιτεί ακριβώς " + FixedWidth + "×" + FixedHeight +
                           " pixels (δόθηκε " + width + "×" + height + ").");
            }

            return errors;
        }

        if (width < MinWidth || width > MaxWidth)
        {
            errors.Add("Το πλάτος πρέπει να είναι μεταξύ " + MinWidth + " και " + MaxWidth + " pixels.");
        }

        if (height < MinHeight || height > MaxHeight)
        {
            errors.Add("Το ύψος πρέπει να είναι μεταξύ " + MinHeight + " και " + MaxHeight + " pixels.");
        }

        if (WidthAlignment > 1 && width % WidthAlignment != 0)
        {
            errors.Add("Το πλάτος πρέπει να είναι πολλαπλάσιο του " + WidthAlignment +
                       " (byte alignment του mode).");
        }

        if (HeightAlignment > 1 && height % HeightAlignment != 0)
        {
            errors.Add("Το ύψος πρέπει να είναι πολλαπλάσιο του " + HeightAlignment + ".");
        }

        return errors;
    }

    public bool IsValid(int width, int height)
    {
        return Validate(width, height).Count == 0;
    }
}
