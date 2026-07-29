using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Imaging;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data;
using RetroTools.Data.Entities;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

/// <summary>Δημιουργία sprite από αρχείο εικόνας.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/sprites")]
public sealed class ImageImportController : RetroApiController
{
    private const int MaxUploadBytes = 8 * 1024 * 1024;

    private readonly RetroToolsDbContext _context;
    private readonly ProjectAccess _access;

    public ImageImportController(RetroToolsDbContext context, ProjectAccess access)
    {
        _context = context;
        _access = access;
    }

    [HttpPost("import-png")]
    [Authorize]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> ImportPng(
        long projectId,
        IFormFile? file,
        [FromQuery] string? name,
        [FromQuery] bool keepPalette,
        CancellationToken cancellationToken)
    {
        var project = await _access.FindWritableAsync(projectId, cancellationToken);

        if (project == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        if (file == null || file.Length == 0)
        {
            return InvalidRequest("Λείπει το αρχείο", "Ανέβασε ένα αρχείο PNG.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return InvalidRequest("Πολύ μεγάλο αρχείο", "Το όριο είναι " + (MaxUploadBytes / (1024 * 1024)) + " MB.");
        }

        byte[] content;

        await using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream, cancellationToken);
            content = stream.ToArray();
        }

        var mode = PlatformCatalog.GetMode(project.ModeCode);
        var platform = PlatformCatalog.Get(project.PlatformCode);

        DecodedImage image;

        try
        {
            image = PngReader.Read(content);
        }
        catch (InvalidDataException exception)
        {
            // Το μήνυμα του decoder είναι ήδη συγκεκριμένο (π.χ. «δεν υποστηρίζονται
            // interlaced PNG»), οπότε περνά αυτούσιο στον χρήστη.
            return InvalidRequest("Δεν διαβάζεται η εικόνα", exception.Message);
        }

        var sizeErrors = mode.SpriteSize.Validate(image.Width, image.Height);

        if (sizeErrors.Count > 0)
        {
            return InvalidRequest(
                "Οι διαστάσεις της εικόνας δεν ταιριάζουν στο " + mode.Name,
                "Η εικόνα είναι " + image.Width + "×" + image.Height + ". " + string.Join(" ", sizeErrors));
        }

        var slotColors = await LoadSlotColorsAsync(projectId, mode, cancellationToken);

        var result = ImageQuantizer.Quantize(image, mode, platform, new ImageImportOptions
        {
            Strategy = keepPalette ? PaletteStrategy.UseProjectPalette : PaletteStrategy.AutoAssign,
            ProjectSlotColors = slotColors,
            PaletteProfileId = project.PaletteProfileId,
        });

        var sprite = new Sprite
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(file.FileName)
                : name.Trim(),
            WidthPx = image.Width,
            HeightPx = image.Height,
            HasMask = mode.SupportsMask,
            SortOrder = await _context.Sprites.CountAsync(s => s.ProjectId == projectId, cancellationToken),
        };

        sprite.Frames.Add(new SpriteFrame
        {
            FrameIndex = 0,
            PixelData = RsprContainer.Write(result.Frame),
        });

        _context.Sprites.Add(sprite);

        if (!keepPalette)
        {
            await SavePaletteAsync(projectId, mode, result.SlotColors, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            id = sprite.Id,
            name = sprite.Name,
            width = sprite.WidthPx,
            height = sprite.HeightPx,
            paletteChanged = !keepPalette,
            warnings = result.Warnings,
        });
    }

    private async Task<int[]> LoadSlotColorsAsync(
        long projectId,
        GraphicsMode mode,
        CancellationToken cancellationToken)
    {
        var slots = DefaultPalettes.For(mode).ToArray();

        var palette = await _context.Palettes.AsNoTracking()
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

        if (palette != null)
        {
            foreach (var entry in palette.Entries.Where(e => e.SlotIndex >= 0 && e.SlotIndex < slots.Length))
            {
                slots[entry.SlotIndex] = entry.HardwareColorIndex;
            }
        }

        return slots;
    }

    /// <summary>
    /// Η αυτόματη ανάθεση αλλάζει την παλέτα ολόκληρου του project. Αυτό είναι
    /// ορατό στον χρήστη μέσω του <c>paletteChanged</c> — δεν πρέπει να τον
    /// αιφνιδιάσει βλέποντας τα υπόλοιπα sprites του να αλλάζουν χρώματα.
    /// </summary>
    private async Task SavePaletteAsync(
        long projectId,
        GraphicsMode mode,
        IReadOnlyList<int> slotColors,
        CancellationToken cancellationToken)
    {
        var palette = await _context.Palettes
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

        if (palette == null)
        {
            palette = new Palette { ProjectId = projectId, Name = "Κύρια" };
            _context.Palettes.Add(palette);
        }

        _context.PaletteEntries.RemoveRange(palette.Entries);

        for (var slot = 0; slot < slotColors.Count; slot++)
        {
            palette.Entries.Add(new PaletteEntry
            {
                SlotIndex = slot,
                HardwareColorIndex = slotColors[slot],
                Role = (int)mode.PixelSlots[slot].Role,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
