using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Export;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

[ApiController]
[Route("api/export")]
public sealed class ExportController : RetroApiController
{
    private readonly RetroToolsDbContext _context;
    private readonly ProjectAccess _access;

    public ExportController(RetroToolsDbContext context, ProjectAccess access)
    {
        _context = context;
        _access = access;
    }

    /// <summary>Ποιες μορφές έχουν νόημα για αυτό το sprite.</summary>
    [HttpGet("sprite/{id:long}/formats")]
    public async Task<IActionResult> Formats(long id, CancellationToken cancellationToken)
    {
        var sprite = await _access.FindReadableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        var project = await _context.Projects.AsNoTracking()
            .SingleAsync(p => p.Id == sprite.ProjectId, cancellationToken);
        var mode = PlatformCatalog.GetMode(project.ModeCode);

        return Ok(SpriteExporters.For(mode)
            .Select(e => new { id = e.FormatId, name = e.DisplayName })
            .ToList());
    }

    [HttpGet("sprite/{id:long}")]
    public async Task<IActionResult> ExportSprite(
        long id,
        [FromQuery] string format,
        [FromQuery] bool includeMask,
        [FromQuery] int scale,
        [FromQuery] int? loadAddress,
        CancellationToken cancellationToken)
    {
        var sprite = await _access.FindReadableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        if (!SpriteExporters.TryGet(format ?? string.Empty, out var exporter) || exporter == null)
        {
            return InvalidRequest(
                "Άγνωστη μορφή",
                "Διαθέσιμες: " + string.Join(", ", SpriteExporters.All.Select(e => e.FormatId)) + ".");
        }

        var project = await _context.Projects.AsNoTracking()
            .SingleAsync(p => p.Id == sprite.ProjectId, cancellationToken);

        var mode = PlatformCatalog.GetMode(project.ModeCode);
        var platform = PlatformCatalog.Get(project.PlatformCode);

        if (!exporter.Supports(mode))
        {
            return InvalidRequest(
                "Μη συμβατή μορφή",
                "Η μορφή '" + exporter.FormatId + "' δεν ισχύει για " + mode.Name + ".");
        }

        var frames = await _context.SpriteFrames.AsNoTracking()
            .Where(f => f.SpriteId == id)
            .OrderBy(f => f.FrameIndex)
            .ToListAsync(cancellationToken);

        if (frames.Count == 0)
        {
            return InvalidRequest("Κενό sprite", "Το sprite δεν έχει καρέ.");
        }

        var source = new SpriteExportSource(
            sprite.Name,
            platform,
            mode,
            frames.Select(f => RsprContainer.Read(f.PixelData)).ToList())
        {
            Masks = frames
                .Where(f => f.MaskData != null)
                .Select(f => RsprContainer.Read(f.MaskData!))
                .ToList(),
            SlotColors = await LoadSlotColorsAsync(sprite.ProjectId, mode, cancellationToken),
            PaletteProfileId = project.PaletteProfileId,
        };

        var options = new ExportOptions
        {
            IncludeMask = includeMask && source.Masks.Count == source.Frames.Count,
            PngScale = scale > 0 ? Math.Min(scale, 16) : 1,
        };

        if (loadAddress.HasValue)
        {
            if (loadAddress.Value < 0 || loadAddress.Value > 0xFFFF)
            {
                return InvalidRequest("Μη έγκυρη διεύθυνση", "Η διεύθυνση φόρτωσης πρέπει να είναι 0–65535.");
            }

            options.LoadAddress = loadAddress.Value;
        }

        var result = exporter.Export(source, options);

        return File(result.Content, result.ContentType, result.FileName);
    }

    private async Task<IReadOnlyList<int>> LoadSlotColorsAsync(
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
}
