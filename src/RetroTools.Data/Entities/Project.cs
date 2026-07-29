namespace RetroTools.Data.Entities;

public enum ProjectVisibility
{
    /// <summary>Μόνο ο ιδιοκτήτης.</summary>
    Private = 0,

    /// <summary>Ορατό σε όποιον έχει τον σύνδεσμο.</summary>
    Unlisted = 1,

    /// <summary>Ορατό σε όλους, μόνο για ανάγνωση.</summary>
    Public = 2,
}

/// <summary>
/// Το aggregate root. Κάθε sprite, palette και spritemap ανήκει σε ένα project,
/// και κάθε project σε έναν χρήστη — εδώ κρέμεται όλος ο έλεγχος πρόσβασης.
/// </summary>
public sealed class Project
{
    public long Id { get; set; }

    public Guid OwnerId { get; set; }

    public User? Owner { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string PlatformCode { get; set; } = string.Empty;

    public PlatformRecord? Platform { get; set; }

    public string ModeCode { get; set; } = string.Empty;

    public PlatformModeRecord? Mode { get; set; }

    /// <summary>Ποιο palette profile βλέπει ο χρήστης (π.χ. "nominal", "d8"). Μόνο προβολή.</summary>
    public string? PaletteProfileId { get; set; }

    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>Optimistic concurrency — αυξάνεται αυτόματα σε κάθε αποθήκευση.</summary>
    public long RowVersion { get; set; }

    public ICollection<Palette> Palettes { get; set; } = new List<Palette>();

    public ICollection<SpriteGroup> Groups { get; set; } = new List<SpriteGroup>();

    public ICollection<Sprite> Sprites { get; set; } = new List<Sprite>();

    public ICollection<SpriteMap> SpriteMaps { get; set; } = new List<SpriteMap>();
}

/// <summary>
/// Η παλέτα ενός project: ποιο χρώμα υλικού δείχνει κάθε slot του mode.
/// Έχει νόημα κυρίως στον CPC (16 pens από 27 χρώματα)· στους C64/ZX καταγράφει
/// ποια χρώματα διάλεξε ο χρήστης για τους καταχωρητές.
/// </summary>
public sealed class Palette
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<PaletteEntry> Entries { get; set; } = new List<PaletteEntry>();
}

public sealed class PaletteEntry
{
    public long PaletteId { get; set; }

    public Palette? Palette { get; set; }

    /// <summary>Το slot / pen του mode.</summary>
    public int SlotIndex { get; set; }

    /// <summary>Δείκτης στην παλέτα υλικού: 0–26 (CPC), 0–15 (C64), 0–15 (ZX).</summary>
    public int HardwareColorIndex { get; set; }

    /// <summary>
    /// Ρόλος του slot, αντιγραμμένος από το <c>PixelSlotRole</c> του mode.
    /// Αποθηκεύεται ώστε το UI να ξέρει ποια slots είναι κοινοί καταχωρητές
    /// χωρίς να ξαναρωτήσει τον catalog.
    /// </summary>
    public int Role { get; set; }
}

public sealed class SpriteGroup
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public ICollection<Sprite> Sprites { get; set; } = new List<Sprite>();
}
