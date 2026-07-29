using System.ComponentModel.DataAnnotations;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Models;

public sealed record ProjectDto(
    long Id,
    string Name,
    string? Description,
    string PlatformCode,
    string ModeCode,
    string? PaletteProfileId,
    string Visibility,
    bool IsOwner,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long RowVersion,
    int SpriteCount)
{
    public static ProjectDto From(Project project, Guid? currentUserId, int spriteCount)
    {
        return new ProjectDto(
            project.Id,
            project.Name,
            project.Description,
            project.PlatformCode,
            project.ModeCode,
            project.PaletteProfileId,
            project.Visibility.ToString(),
            currentUserId.HasValue && project.OwnerId == currentUserId.Value,
            project.CreatedUtc,
            project.UpdatedUtc,
            project.RowVersion,
            spriteCount);
    }
}

public sealed class CreateProjectRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// Δεν χρειάζεται ξεχωριστό platformCode: το mode το συνεπάγεται
    /// (π.χ. "cpc.mode0" → "cpc"), οπότε δεν μπορούν να έρθουν σε αντίφαση.
    /// </summary>
    [Required]
    [StringLength(32)]
    public string ModeCode { get; set; } = string.Empty;

    [StringLength(32)]
    public string? PaletteProfileId { get; set; }
}

public sealed class UpdateProjectRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    [StringLength(32)]
    public string? PaletteProfileId { get; set; }

    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;

    /// <summary>Η έκδοση που είχε ο client — αν δεν ταιριάζει, η ενημέρωση απορρίπτεται.</summary>
    public long? RowVersion { get; set; }
}
