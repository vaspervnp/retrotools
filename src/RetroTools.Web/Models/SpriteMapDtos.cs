using System.ComponentModel.DataAnnotations;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Models;

public sealed record SpriteMapDto(
    long Id,
    long ProjectId,
    string Name,
    int Columns,
    int Rows,
    int CellWidthPx,
    int CellHeightPx,
    DateTime UpdatedUtc,
    long RowVersion,
    IReadOnlyList<SpriteMapCellDto> Cells)
{
    public static SpriteMapDto From(SpriteMap map)
    {
        return new SpriteMapDto(
            map.Id,
            map.ProjectId,
            map.Name,
            map.Columns,
            map.Rows,
            map.CellWidthPx,
            map.CellHeightPx,
            map.UpdatedUtc,
            map.RowVersion,
            map.Cells
                .OrderBy(c => c.Row).ThenBy(c => c.Column)
                .Select(c => new SpriteMapCellDto(
                    c.Column,
                    c.Row,
                    c.SpriteId,
                    c.FrameIndex,
                    c.Flags.HasFlag(SpriteMapCellFlags.FlipHorizontal),
                    c.Flags.HasFlag(SpriteMapCellFlags.FlipVertical)))
                .ToList());
    }
}

public sealed record SpriteMapCellDto(
    int Column,
    int Row,
    long? SpriteId,
    int FrameIndex,
    bool FlipHorizontal,
    bool FlipVertical);

public sealed class CreateSpriteMapRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 64)]
    public int Columns { get; set; } = 4;

    [Range(1, 64)]
    public int Rows { get; set; } = 4;

    [Range(1, 512)]
    public int CellWidthPx { get; set; } = 16;

    [Range(1, 512)]
    public int CellHeightPx { get; set; } = 16;
}

public sealed class UpdateSpriteMapRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 64)]
    public int Columns { get; set; }

    [Range(1, 64)]
    public int Rows { get; set; }

    [Range(1, 512)]
    public int CellWidthPx { get; set; }

    [Range(1, 512)]
    public int CellHeightPx { get; set; }

    public long? RowVersion { get; set; }

    /// <summary>
    /// Τα κελιά αντικαθίστανται ολόκληρα. Κελιά με <c>spriteId: null</c> παραλείπονται —
    /// δεν αποθηκεύουμε άδειες γραμμές.
    /// </summary>
    public IReadOnlyList<SpriteMapCellDto> Cells { get; set; } = Array.Empty<SpriteMapCellDto>();
}
