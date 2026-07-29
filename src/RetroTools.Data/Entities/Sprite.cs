namespace RetroTools.Data.Entities;

public sealed class Sprite
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public long? GroupId { get; set; }

    public SpriteGroup? Group { get; set; }

    public string Name { get; set; } = string.Empty;

    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    public long? PaletteId { get; set; }

    public Palette? Palette { get; set; }

    public bool HasMask { get; set; }

    /// <summary>
    /// Επιπλέον χαρακτηριστικά ανά πλατφόρμα σε JSON — π.χ. για C64 hardware sprite:
    /// χρώμα sprite, expandX/expandY, multicolor. Δεν αξίζει δικές τους στήλες
    /// αφού διαφέρουν ριζικά ανά πλατφόρμα.
    /// </summary>
    public string? MetaJson { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public long RowVersion { get; set; }

    public ICollection<SpriteFrame> Frames { get; set; } = new List<SpriteFrame>();
}

/// <summary>
/// Ένα καρέ. Τα pixels αποθηκεύονται ως <c>RSPR</c> container (indexed, deflate),
/// όχι ως packed δεδομένα πλατφόρμας — έτσι η αλλαγή mode δεν καταστρέφει τη δουλειά.
/// </summary>
public sealed class SpriteFrame
{
    public long Id { get; set; }

    public long SpriteId { get; set; }

    public Sprite? Sprite { get; set; }

    public int FrameIndex { get; set; }

    public int DurationMs { get; set; } = 100;

    /// <summary>RSPR container με το indexed buffer.</summary>
    public byte[] PixelData { get; set; } = Array.Empty<byte>();

    /// <summary>Attributes ZX Spectrum, ένα byte ανά κελί 8×8. Null στις άλλες πλατφόρμες.</summary>
    public byte[]? AttributeData { get; set; }

    /// <summary>Μάσκα διαφάνειας ως RSPR (1 = αδιαφανές). Null όταν δεν χρησιμοποιείται.</summary>
    public byte[]? MaskData { get; set; }
}
