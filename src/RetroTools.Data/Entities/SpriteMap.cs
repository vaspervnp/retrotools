namespace RetroTools.Data.Entities;

[Flags]
public enum SpriteMapCellFlags
{
    None = 0,
    FlipHorizontal = 1,
    FlipVertical = 2,
}

/// <summary>
/// Πλέγμα από κελιά που δείχνουν σε sprites: animation strip, tileset ή character set.
/// </summary>
public sealed class SpriteMap
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Columns { get; set; }

    public int Rows { get; set; }

    public int CellWidthPx { get; set; }

    public int CellHeightPx { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public long RowVersion { get; set; }

    public ICollection<SpriteMapCell> Cells { get; set; } = new List<SpriteMapCell>();
}

public sealed class SpriteMapCell
{
    public long SpriteMapId { get; set; }

    public SpriteMap? SpriteMap { get; set; }

    public int Column { get; set; }

    public int Row { get; set; }

    /// <summary>Null = άδειο κελί.</summary>
    public long? SpriteId { get; set; }

    public Sprite? Sprite { get; set; }

    public int FrameIndex { get; set; }

    public SpriteMapCellFlags Flags { get; set; }
}
