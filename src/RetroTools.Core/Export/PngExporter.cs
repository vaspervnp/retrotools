using RetroTools.Core.Imaging;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Export;

/// <summary>
/// Εξαγωγή PNG με σωστή αναλογία pixel — για τεκμηρίωση, mockups ή εισαγωγή
/// σε άλλα εργαλεία pixel art.
/// </summary>
public sealed class PngExporter : ISpriteExporter
{
    public string FormatId
    {
        get { return "png"; }
    }

    public string DisplayName
    {
        get { return "PNG εικόνα"; }
    }

    public bool Supports(GraphicsMode mode)
    {
        return true;
    }

    public ExportResult Export(SpriteExportSource source, ExportOptions options)
    {
        var profile = source.Platform.Palette.GetProfile(source.PaletteProfileId);
        var palette = new Rgb24[Math.Max(1, source.SlotColors.Count)];
        int? transparent = null;

        for (var slot = 0; slot < palette.Length; slot++)
        {
            if (slot < source.Mode.PixelSlots.Count
                && source.Mode.PixelSlots[slot].Role == PixelSlotRole.Transparent)
            {
                transparent = slot;
                palette[slot] = new Rgb24(0, 0, 0);
                continue;
            }

            palette[slot] = slot < source.SlotColors.Count
                ? profile[source.SlotColors[slot]]
                : new Rgb24(0, 0, 0);
        }

        var scale = Math.Max(1, options.PngScale);

        // Η αναλογία pixel μπαίνει στην κλίμακα: αλλιώς ένα CPC Mode 0 sprite
        // θα εξαγόταν στενόμακρο σε σχέση με το πώς φαίνεται στην οθόνη.
        var bytes = PngWriter.WriteIndexed(
            source.Frames[0],
            palette,
            scale * source.Mode.PixelAspect.Width,
            scale * source.Mode.PixelAspect.Height,
            transparent);

        return new ExportResult(source.Identifier + ".png", "image/png", bytes);
    }
}

/// <summary>Μητρώο των διαθέσιμων μορφών εξαγωγής.</summary>
public static class SpriteExporters
{
    private static readonly ISpriteExporter[] Registry =
    {
        new BinaryExporter(),
        new Z80AsmExporter(),
        new Acme6502Exporter(),
        new PrgExporter(),
        new CHeaderExporter(),
        new PngExporter(),
    };

    public static IReadOnlyList<ISpriteExporter> All
    {
        get { return Registry; }
    }

    /// <summary>Οι μορφές που έχουν νόημα για το συγκεκριμένο mode.</summary>
    public static IReadOnlyList<ISpriteExporter> For(GraphicsMode mode)
    {
        return Registry.Where(e => e.Supports(mode)).ToList();
    }

    public static ISpriteExporter Get(string formatId)
    {
        var exporter = Registry.FirstOrDefault(
            e => string.Equals(e.FormatId, formatId, StringComparison.OrdinalIgnoreCase));

        if (exporter == null)
        {
            throw new KeyNotFoundException(
                "Άγνωστη μορφή '" + formatId + "'. Διαθέσιμες: " +
                string.Join(", ", Registry.Select(e => e.FormatId)) + ".");
        }

        return exporter;
    }

    public static bool TryGet(string formatId, out ISpriteExporter? exporter)
    {
        exporter = Registry.FirstOrDefault(
            e => string.Equals(e.FormatId, formatId, StringComparison.OrdinalIgnoreCase));

        return exporter != null;
    }
}
