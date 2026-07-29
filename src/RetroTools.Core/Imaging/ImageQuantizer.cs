using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Imaging;

public enum PaletteStrategy
{
    /// <summary>Επιλέγει αυτόματα τα καταλληλότερα χρώματα υλικού για την εικόνα.</summary>
    AutoAssign = 0,

    /// <summary>Κρατά την υπάρχουσα παλέτα του project και ταιριάζει σε αυτήν.</summary>
    UseProjectPalette = 1,
}

public sealed class ImageImportOptions
{
    public PaletteStrategy Strategy { get; set; } = PaletteStrategy.AutoAssign;

    /// <summary>Κάτω από αυτό το alpha το pixel θεωρείται διαφανές.</summary>
    public byte AlphaThreshold { get; set; } = 128;

    /// <summary>Η τρέχουσα παλέτα, όταν η στρατηγική είναι <see cref="PaletteStrategy.UseProjectPalette"/>.</summary>
    public IReadOnlyList<int>? ProjectSlotColors { get; set; }

    public string? PaletteProfileId { get; set; }
}

public sealed class ImageImportResult
{
    public ImageImportResult(FrameBuffer frame, IReadOnlyList<int> slotColors, IReadOnlyList<string> warnings)
    {
        Frame = frame;
        SlotColors = slotColors;
        Warnings = warnings;
    }

    public FrameBuffer Frame { get; }

    /// <summary>Δείκτης χρώματος υλικού ανά slot — μπορεί να έχει αλλάξει από την αυτόματη ανάθεση.</summary>
    public IReadOnlyList<int> SlotColors { get; }

    /// <summary>
    /// Τι χάθηκε ή τι παραβιάζει τους περιορισμούς. Δεν εμποδίζει την εισαγωγή:
    /// ο χρήστης βλέπει τι έγινε και αποφασίζει.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Μετατρέπει μια εικόνα RGBA σε indexed buffer της πλατφόρμας.
/// </summary>
/// <remarks>
/// Η απόσταση χρωμάτων μετριέται σε <b>γραμμικό</b> χώρο με συντελεστές φωτεινότητας
/// (βλ. <see cref="Rgb24.LinearDistanceSquaredTo"/>). Στον χώρο sRGB το 0x80 δεν είναι
/// οπτικά μισό, οπότε μια απλή ευκλείδεια απόσταση θα διάλεγε συστηματικά λάθος
/// αποχρώσεις — ιδίως στα μεσαία επίπεδα του CPC, που είναι ακριβώς εκεί.
/// </remarks>
public static class ImageQuantizer
{
    public static ImageImportResult Quantize(
        DecodedImage image,
        GraphicsMode mode,
        PlatformDefinition platform,
        ImageImportOptions options)
    {
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        options ??= new ImageImportOptions();

        var profile = platform.Palette.GetProfile(options.PaletteProfileId);
        var slotCount = mode.MaxPixelValue + 1;
        var warnings = new List<string>();

        var transparentSlot = FindTransparentSlot(mode);

        var slotColors = options.Strategy == PaletteStrategy.UseProjectPalette
            ? ResolveProjectPalette(options, mode)
            : AutoAssign(image, mode, platform, profile, options, transparentSlot, warnings);

        var slotRgb = new Rgb24[slotCount];

        for (var slot = 0; slot < slotCount; slot++)
        {
            slotRgb[slot] = profile[slotColors[slot]];
        }

        var frame = new FrameBuffer(image.Width, image.Height);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];

                if (transparentSlot.HasValue && pixel.A < options.AlphaThreshold)
                {
                    frame[x, y] = (byte)transparentSlot.Value;
                    continue;
                }

                frame[x, y] = (byte)FindNearestSlot(pixel.ToRgb(), slotRgb, transparentSlot);
            }
        }

        ReportColorLoss(image, slotRgb, options, warnings);
        ReportCellColorLoss(image, mode, options, warnings);

        return new ImageImportResult(frame, slotColors, warnings);
    }

    private static int? FindTransparentSlot(GraphicsMode mode)
    {
        for (var slot = 0; slot < mode.PixelSlots.Count; slot++)
        {
            if (mode.PixelSlots[slot].Role == PixelSlotRole.Transparent)
            {
                return slot;
            }
        }

        return null;
    }

    private static int[] ResolveProjectPalette(ImageImportOptions options, GraphicsMode mode)
    {
        var slots = DefaultPalettes.For(mode).ToArray();

        if (options.ProjectSlotColors != null)
        {
            for (var slot = 0; slot < slots.Length && slot < options.ProjectSlotColors.Count; slot++)
            {
                slots[slot] = options.ProjectSlotColors[slot];
            }
        }

        return slots;
    }

    /// <summary>
    /// Διαλέγει τα χρώματα υλικού που καλύπτουν καλύτερα την εικόνα, κατά συχνότητα.
    /// </summary>
    /// <remarks>
    /// Πρώτα κάθε χρώμα της εικόνας στρογγυλοποιείται στο πλησιέστερο χρώμα υλικού και
    /// μετά μετριούνται τα pixels ανά χρώμα υλικού. Το αντίστροφο — να διαλέξουμε πρώτα
    /// τα δημοφιλέστερα χρώματα της εικόνας — θα σπαταλούσε slots σε αποχρώσεις που
    /// ούτως ή άλλως καταλήγουν στο ίδιο χρώμα υλικού.
    /// </remarks>
    private static int[] AutoAssign(
        DecodedImage image,
        GraphicsMode mode,
        PlatformDefinition platform,
        PaletteProfile profile,
        ImageImportOptions options,
        int? transparentSlot,
        List<string> warnings)
    {
        var counts = new Dictionary<int, int>();
        var cache = new Dictionary<Rgb24, int>();

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];

                if (pixel.A < options.AlphaThreshold)
                {
                    continue;
                }

                var rgb = pixel.ToRgb();

                if (!cache.TryGetValue(rgb, out var hardware))
                {
                    hardware = platform.Palette.FindNearest(rgb, profile.Id);
                    cache[rgb] = hardware;
                }

                counts[hardware] = counts.TryGetValue(hardware, out var existing) ? existing + 1 : 1;
            }
        }

        var slotCount = mode.MaxPixelValue + 1;
        var slots = DefaultPalettes.For(mode).ToArray();

        var assignable = Enumerable.Range(0, slotCount)
            .Where(slot => slot != transparentSlot)
            .ToList();

        var ranked = counts.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).ToList();

        if (ranked.Count > assignable.Count)
        {
            warnings.Add(
                "Η εικόνα χρειάζεται " + ranked.Count + " χρώματα υλικού, το " + mode.Name +
                " έχει " + assignable.Count + " διαθέσιμα slots — τα υπόλοιπα στρογγυλοποιήθηκαν στα πλησιέστερα.");
        }

        for (var i = 0; i < assignable.Count && i < ranked.Count; i++)
        {
            slots[assignable[i]] = ranked[i];
        }

        return slots;
    }

    private static int FindNearestSlot(Rgb24 color, Rgb24[] slotRgb, int? transparentSlot)
    {
        var best = transparentSlot == 0 && slotRgb.Length > 1 ? 1 : 0;
        var bestDistance = double.MaxValue;

        for (var slot = 0; slot < slotRgb.Length; slot++)
        {
            // Το slot διαφάνειας δεν είναι χρώμα — ένα αδιαφανές pixel δεν επιτρέπεται
            // να καταλήξει εκεί επειδή «έμοιαζε» με ό,τι τυχαία έχει ανατεθεί σε αυτό.
            if (slot == transparentSlot)
            {
                continue;
            }

            var distance = color.LinearDistanceSquaredTo(slotRgb[slot]);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = slot;
            }
        }

        return best;
    }

    private static void ReportColorLoss(
        DecodedImage image,
        Rgb24[] slotRgb,
        ImageImportOptions options,
        List<string> warnings)
    {
        var distinct = new HashSet<Rgb24>();

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];

                if (pixel.A >= options.AlphaThreshold)
                {
                    distinct.Add(pixel.ToRgb());
                }
            }
        }

        if (distinct.Count > slotRgb.Length)
        {
            warnings.Add(
                "Η εικόνα έχει " + distinct.Count + " διαφορετικά χρώματα και μετατράπηκε σε " +
                slotRgb.Length + ".");
        }
    }

    /// <summary>
    /// Πόσα κελιά έχασαν χρώματα λόγω του ορίου ανά κελί (attribute clash).
    /// </summary>
    /// <remarks>
    /// Μετρώνται τα χρώματα της <b>πηγαίας εικόνας</b> μέσα σε κάθε κελί, όχι τα slots
    /// του αποτελέσματος. Το αποτέλεσμα δεν μπορεί ποτέ να παραβιάσει το όριο: σε
    /// per-cell modes το indexed buffer κρατά ακριβώς <c>MaxColorsPerCell</c> δυνατές
    /// τιμές (0 = PAPER, 1 = INK στο Spectrum), οπότε το clash είναι <b>δομικά αδύνατο</b>
    /// στο μοντέλο μας. Η πραγματική απώλεια συμβαίνει εδώ, στη μετατροπή.
    /// </remarks>
    private static void ReportCellColorLoss(
        DecodedImage image,
        GraphicsMode mode,
        ImageImportOptions options,
        List<string> warnings)
    {
        if (mode.ColorScope != ColorScope.PerCell || mode.CellWidth == 0 || mode.CellHeight == 0)
        {
            return;
        }

        var columns = (image.Width + mode.CellWidth - 1) / mode.CellWidth;
        var rows = (image.Height + mode.CellHeight - 1) / mode.CellHeight;
        var offending = 0;
        var worst = 0;

        for (var cellY = 0; cellY < rows; cellY++)
        {
            for (var cellX = 0; cellX < columns; cellX++)
            {
                var used = new HashSet<Rgb24>();

                for (var y = cellY * mode.CellHeight; y < Math.Min((cellY + 1) * mode.CellHeight, image.Height); y++)
                {
                    for (var x = cellX * mode.CellWidth; x < Math.Min((cellX + 1) * mode.CellWidth, image.Width); x++)
                    {
                        var pixel = image[x, y];

                        if (pixel.A >= options.AlphaThreshold)
                        {
                            used.Add(pixel.ToRgb());
                        }
                    }
                }

                if (used.Count > mode.MaxColorsPerCell)
                {
                    offending++;
                    worst = Math.Max(worst, used.Count);
                }
            }
        }

        if (offending > 0)
        {
            warnings.Add(
                offending + " κελιά " + mode.CellWidth + "×" + mode.CellHeight +
                " της εικόνας είχαν πάνω από " + mode.MaxColorsPerCell +
                " χρώματα (έως " + worst + ") — το υλικό επιτρέπει " + mode.MaxColorsPerCell +
                " ανά κελί, οπότε τα υπόλοιπα χάθηκαν (attribute clash).");
        }
    }
}
