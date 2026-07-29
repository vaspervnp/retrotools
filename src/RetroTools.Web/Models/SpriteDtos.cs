using System.ComponentModel.DataAnnotations;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Models;

public sealed record SpriteDto(
    long Id,
    long ProjectId,
    long? GroupId,
    string Name,
    int WidthPx,
    int HeightPx,
    bool HasMask,
    string? MetaJson,
    int SortOrder,
    int FrameCount,
    DateTime UpdatedUtc,
    long RowVersion)
{
    public static SpriteDto From(Sprite sprite, int frameCount)
    {
        return new SpriteDto(
            sprite.Id,
            sprite.ProjectId,
            sprite.GroupId,
            sprite.Name,
            sprite.WidthPx,
            sprite.HeightPx,
            sprite.HasMask,
            sprite.MetaJson,
            sprite.SortOrder,
            frameCount,
            sprite.UpdatedUtc,
            sprite.RowVersion);
    }
}

public sealed class CreateSpriteRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 512)]
    public int WidthPx { get; set; }

    [Range(1, 512)]
    public int HeightPx { get; set; }

    public long? GroupId { get; set; }

    public bool HasMask { get; set; }

    public string? MetaJson { get; set; }
}

public sealed class UpdateSpriteRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public long? GroupId { get; set; }

    public bool HasMask { get; set; }

    public string? MetaJson { get; set; }

    public int SortOrder { get; set; }

    public long? RowVersion { get; set; }
}

/// <summary>
/// Τα pixels ταξιδεύουν ως base64 του <b>indexed</b> buffer (1 byte ανά pixel),
/// όχι ως packed δεδομένα πλατφόρμας: ο editor δουλεύει σε δείκτες και η μετατροπή
/// σε bytes υλικού γίνεται μόνο στο export.
/// </summary>
public sealed record SpriteFrameDto(
    int FrameIndex,
    int DurationMs,
    int Width,
    int Height,
    string Pixels,
    string? Attributes,
    string? Mask);

public sealed class SaveFrameRequest
{
    [Range(1, 10000)]
    public int DurationMs { get; set; } = 100;

    /// <summary>Base64 του indexed buffer, μήκους ακριβώς width × height.</summary>
    [Required]
    public string Pixels { get; set; } = string.Empty;

    /// <summary>Base64 των ZX attributes, ένα byte ανά κελί 8×8.</summary>
    public string? Attributes { get; set; }

    /// <summary>Base64 της μάσκας (1 byte ανά pixel, 1 = αδιαφανές).</summary>
    public string? Mask { get; set; }
}
