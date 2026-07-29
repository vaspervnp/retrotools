using RetroTools.Core.Model;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Export;

/// <summary>
/// Ό,τι χρειάζεται ένας exporter, ανεξάρτητα από τη βάση δεδομένων.
/// Το <c>RetroTools.Core</c> δεν γνωρίζει EF entities.
/// </summary>
public sealed class SpriteExportSource
{
    public SpriteExportSource(
        string name,
        PlatformDefinition platform,
        GraphicsMode mode,
        IReadOnlyList<FrameBuffer> frames)
    {
        if (frames == null || frames.Count == 0)
        {
            throw new ArgumentException("Χρειάζεται τουλάχιστον ένα καρέ.", nameof(frames));
        }

        Name = name;
        Platform = platform;
        Mode = mode;
        Frames = frames;
    }

    public string Name { get; }

    public PlatformDefinition Platform { get; }

    public GraphicsMode Mode { get; }

    public IReadOnlyList<FrameBuffer> Frames { get; }

    /// <summary>Μάσκες διαφάνειας ανά καρέ (1 = αδιαφανές). Κενό αν δεν υπάρχουν.</summary>
    public IReadOnlyList<FrameBuffer> Masks { get; init; } = Array.Empty<FrameBuffer>();

    /// <summary>ZX attributes ανά καρέ. Κενό στις άλλες πλατφόρμες.</summary>
    public IReadOnlyList<AttributeGrid> Attributes { get; init; } = Array.Empty<AttributeGrid>();

    /// <summary>Δείκτης χρώματος υλικού ανά slot της παλέτας.</summary>
    public IReadOnlyList<int> SlotColors { get; init; } = Array.Empty<int>();

    public string? PaletteProfileId { get; init; }

    public int Width
    {
        get { return Frames[0].Width; }
    }

    public int Height
    {
        get { return Frames[0].Height; }
    }

    /// <summary>Αναγνωριστικό κατάλληλο για label assembler ή όνομα μεταβλητής C.</summary>
    public string Identifier
    {
        get
        {
            var characters = Name
                .Select(c => char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '_')
                .ToArray();

            var identifier = new string(characters).Trim('_');

            // Ονόματα εξ ολοκλήρου εκτός ASCII (π.χ. ελληνικά) εξαφανίζονται εντελώς·
            // χωρίς αυτόν τον έλεγχο το label θα έβγαινε "sprite_" με κρεμασμένη παύλα.
            if (identifier.Length == 0)
            {
                return "sprite";
            }

            // Οι assemblers και η C δεν δέχονται αναγνωριστικό που αρχίζει με ψηφίο.
            return char.IsDigit(identifier[0]) ? "sprite_" + identifier : identifier;
        }
    }
}

public sealed class ExportOptions
{
    /// <summary>Πόσα bytes ανά γραμμή στον παραγόμενο πηγαίο κώδικα.</summary>
    public int BytesPerLine { get; set; } = 8;

    /// <summary>Συμπερίληψη της μάσκας (όπου υπάρχει) μετά τα δεδομένα κάθε καρέ.</summary>
    public bool IncludeMask { get; set; }

    /// <summary>Διεύθυνση φόρτωσης για μορφές που τη χρειάζονται (π.χ. C64 <c>.prg</c>).</summary>
    public int LoadAddress { get; set; } = 0x2000;

    /// <summary>Κλίμακα για την εξαγωγή PNG.</summary>
    public int PngScale { get; set; } = 1;
}

public sealed record ExportResult(string FileName, string ContentType, byte[] Content)
{
    public string AsText()
    {
        return System.Text.Encoding.UTF8.GetString(Content);
    }
}

public interface ISpriteExporter
{
    /// <summary>Σταθερό αναγνωριστικό για το query string του API.</summary>
    string FormatId { get; }

    string DisplayName { get; }

    bool Supports(GraphicsMode mode);

    ExportResult Export(SpriteExportSource source, ExportOptions options);
}
