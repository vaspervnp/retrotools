namespace RetroTools.Data.Entities;

/// <summary>
/// Αντίγραφο των δεδομένων υλικού στη βάση, για referential integrity.
/// <b>Η πηγή αλήθειας παραμένει το <c>PlatformCatalog</c> στον κώδικα</b> —
/// αυτοί οι πίνακες γεμίζουν με seed από εκεί σε κάθε εκκίνηση.
/// </summary>
public sealed class PlatformRecord
{
    /// <summary>"cpc", "c64", "zx".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public int Year { get; set; }

    public int ColorCount { get; set; }

    public bool HasHardwareSprites { get; set; }

    public bool HasProgrammablePalette { get; set; }

    public ICollection<PlatformModeRecord> Modes { get; set; } = new List<PlatformModeRecord>();
}

/// <summary>Ένα γραφικό mode, π.χ. "cpc.mode0".</summary>
public sealed class PlatformModeRecord
{
    public string Code { get; set; } = string.Empty;

    public string PlatformCode { get; set; } = string.Empty;

    public PlatformRecord? Platform { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public int BitsPerPixel { get; set; }

    public int PaletteSlots { get; set; }

    public int MaxColorsPerCell { get; set; }

    /// <summary>Αντιστοιχεί στο <c>RetroTools.Core.Platforms.ColorScope</c>.</summary>
    public int ColorScope { get; set; }

    public int CellWidth { get; set; }

    public int CellHeight { get; set; }

    public int PixelAspectWidth { get; set; }

    public int PixelAspectHeight { get; set; }

    public int WidthAlignment { get; set; }

    public int HeightAlignment { get; set; }

    public int? FixedWidth { get; set; }

    public int? FixedHeight { get; set; }

    public bool IsHardwareSprite { get; set; }

    public bool SupportsMask { get; set; }
}
