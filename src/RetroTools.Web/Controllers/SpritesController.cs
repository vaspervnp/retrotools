using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Model;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data;
using RetroTools.Data.Entities;
using RetroTools.Web.Models;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

[ApiController]
public sealed class SpritesController : RetroApiController
{
    private readonly RetroToolsDbContext _context;
    private readonly ProjectAccess _access;

    public SpritesController(RetroToolsDbContext context, ProjectAccess access)
    {
        _context = context;
        _access = access;
    }

    // --- Λίστα & δημιουργία μέσα σε project ---------------------------------

    [HttpGet("api/projects/{projectId:long}/sprites")]
    public async Task<ActionResult<IReadOnlyList<SpriteDto>>> List(
        long projectId,
        CancellationToken cancellationToken)
    {
        if (await _access.FindReadableAsync(projectId, cancellationToken) == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var sprites = await _context.Sprites
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => new { Sprite = s, FrameCount = s.Frames.Count })
            .ToListAsync(cancellationToken);

        return Ok(sprites.Select(s => SpriteDto.From(s.Sprite, s.FrameCount)).ToList());
    }

    [HttpPost("api/projects/{projectId:long}/sprites")]
    [Authorize]
    public async Task<ActionResult<SpriteDto>> Create(
        long projectId,
        CreateSpriteRequest request,
        CancellationToken cancellationToken)
    {
        var project = await _access.FindWritableAsync(projectId, cancellationToken);

        if (project == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var mode = PlatformCatalog.GetMode(project.ModeCode);

        // Οι διαστάσεις ελέγχονται εδώ και όχι μόνο στο UI: ένα C64 hardware sprite
        // 16×16 δεν υπάρχει, και θα έσπαγε στο export αντί για τη δημιουργία.
        var errors = mode.SpriteSize.Validate(request.WidthPx, request.HeightPx);

        if (errors.Count > 0)
        {
            return InvalidRequest(
                "Μη έγκυρες διαστάσεις για " + mode.Name,
                string.Join(" ", errors));
        }

        if (request.GroupId.HasValue
            && !await _context.SpriteGroups.AnyAsync(
                g => g.Id == request.GroupId.Value && g.ProjectId == projectId, cancellationToken))
        {
            return InvalidRequest("Άγνωστη ομάδα", "Η ομάδα δεν ανήκει σε αυτό το project.");
        }

        var sprite = new Sprite
        {
            ProjectId = projectId,
            GroupId = request.GroupId,
            Name = request.Name.Trim(),
            WidthPx = request.WidthPx,
            HeightPx = request.HeightPx,
            HasMask = request.HasMask && mode.SupportsMask,
            MetaJson = request.MetaJson,
            SortOrder = await _context.Sprites.CountAsync(s => s.ProjectId == projectId, cancellationToken),
        };

        // Κάθε sprite γεννιέται με ένα άδειο καρέ — αλλιώς ο editor θα άνοιγε σε κενό.
        sprite.Frames.Add(new SpriteFrame
        {
            FrameIndex = 0,
            PixelData = RsprContainer.Write(new FrameBuffer(request.WidthPx, request.HeightPx)),
        });

        _context.Sprites.Add(sprite);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = sprite.Id }, SpriteDto.From(sprite, 1));
    }

    // --- Ένα sprite ----------------------------------------------------------

    [HttpGet("api/sprites/{id:long}")]
    public async Task<ActionResult<SpriteDto>> Get(long id, CancellationToken cancellationToken)
    {
        var sprite = await _access.FindReadableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        var frameCount = await _context.SpriteFrames.CountAsync(f => f.SpriteId == id, cancellationToken);

        return Ok(SpriteDto.From(sprite, frameCount));
    }

    [HttpPut("api/sprites/{id:long}")]
    [Authorize]
    public async Task<ActionResult<SpriteDto>> Update(
        long id,
        UpdateSpriteRequest request,
        CancellationToken cancellationToken)
    {
        var sprite = await _access.FindWritableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        if (request.RowVersion.HasValue && request.RowVersion.Value != sprite.RowVersion)
        {
            return Conflict(
                "Το sprite άλλαξε στο μεταξύ",
                "Η έκδοσή σου είναι " + request.RowVersion.Value + ", η τρέχουσα " + sprite.RowVersion + ".");
        }

        if (request.GroupId.HasValue
            && !await _context.SpriteGroups.AnyAsync(
                g => g.Id == request.GroupId.Value && g.ProjectId == sprite.ProjectId, cancellationToken))
        {
            return InvalidRequest("Άγνωστη ομάδα", "Η ομάδα δεν ανήκει στο project του sprite.");
        }

        sprite.Name = request.Name.Trim();
        sprite.GroupId = request.GroupId;
        sprite.HasMask = request.HasMask;
        sprite.MetaJson = request.MetaJson;
        sprite.SortOrder = request.SortOrder;

        await _context.SaveChangesAsync(cancellationToken);

        var frameCount = await _context.SpriteFrames.CountAsync(f => f.SpriteId == id, cancellationToken);

        return Ok(SpriteDto.From(sprite, frameCount));
    }

    [HttpDelete("api/sprites/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var sprite = await _access.FindWritableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        _context.Sprites.Remove(sprite);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // --- Καρέ ----------------------------------------------------------------

    [HttpGet("api/sprites/{id:long}/frames")]
    public async Task<ActionResult<IReadOnlyList<SpriteFrameDto>>> ListFrames(
        long id,
        CancellationToken cancellationToken)
    {
        var sprite = await _access.FindReadableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        var frames = await _context.SpriteFrames
            .AsNoTracking()
            .Where(f => f.SpriteId == id)
            .OrderBy(f => f.FrameIndex)
            .ToListAsync(cancellationToken);

        return Ok(frames.Select(f => ToDto(f, sprite)).ToList());
    }

    [HttpGet("api/sprites/{id:long}/frames/{index:int}")]
    public async Task<ActionResult<SpriteFrameDto>> GetFrame(
        long id,
        int index,
        CancellationToken cancellationToken)
    {
        var sprite = await _access.FindReadableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        var frame = await _context.SpriteFrames
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.SpriteId == id && f.FrameIndex == index, cancellationToken);

        if (frame == null)
        {
            return NotFoundOrForbidden("Το καρέ");
        }

        return Ok(ToDto(frame, sprite));
    }

    [HttpPut("api/sprites/{id:long}/frames/{index:int}")]
    [Authorize]
    public async Task<ActionResult<SpriteFrameDto>> SaveFrame(
        long id,
        int index,
        SaveFrameRequest request,
        CancellationToken cancellationToken)
    {
        var sprite = await _access.FindWritableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        if (index < 0 || index > 255)
        {
            return InvalidRequest("Μη έγκυρο καρέ", "Ο δείκτης καρέ πρέπει να είναι 0–255.");
        }

        var project = await _context.Projects.AsNoTracking()
            .SingleAsync(p => p.Id == sprite.ProjectId, cancellationToken);
        var mode = PlatformCatalog.GetMode(project.ModeCode);

        var validation = ValidatePixels(request.Pixels, sprite, mode, out var pixels);

        if (validation != null)
        {
            return validation;
        }

        byte[]? attributes = null;

        if (!string.IsNullOrEmpty(request.Attributes))
        {
            if (!TryDecodeBase64(request.Attributes, out attributes, out var attributeError))
            {
                return InvalidRequest("Μη έγκυρα attributes", attributeError!);
            }

            var expected = ExpectedAttributeCount(sprite, mode);

            if (expected == 0)
            {
                return InvalidRequest(
                    "Το mode δεν χρησιμοποιεί attributes",
                    "Το " + mode.Name + " δεν έχει χρώμα ανά κελί.");
            }

            if (attributes.Length != expected)
            {
                return InvalidRequest(
                    "Λάθος πλήθος attributes",
                    "Για " + sprite.WidthPx + "×" + sprite.HeightPx + " σε " + mode.Name +
                    " χρειάζονται " + expected + " bytes, δόθηκαν " + attributes.Length + ".");
            }
        }

        byte[]? mask = null;

        if (!string.IsNullOrEmpty(request.Mask))
        {
            if (!TryDecodeBase64(request.Mask, out var maskPixels, out var maskError))
            {
                return InvalidRequest("Μη έγκυρη μάσκα", maskError!);
            }

            if (maskPixels.Length != sprite.WidthPx * sprite.HeightPx)
            {
                return InvalidRequest(
                    "Λάθος μέγεθος μάσκας",
                    "Η μάσκα πρέπει να έχει " + (sprite.WidthPx * sprite.HeightPx) + " bytes.");
            }

            mask = RsprContainer.Write(FrameBuffer.FromPixels(sprite.WidthPx, sprite.HeightPx, maskPixels));
        }

        var frame = await _context.SpriteFrames
            .SingleOrDefaultAsync(f => f.SpriteId == id && f.FrameIndex == index, cancellationToken);

        if (frame == null)
        {
            frame = new SpriteFrame { SpriteId = id, FrameIndex = index };
            _context.SpriteFrames.Add(frame);
        }

        frame.DurationMs = request.DurationMs;
        frame.PixelData = RsprContainer.Write(FrameBuffer.FromPixels(sprite.WidthPx, sprite.HeightPx, pixels));
        frame.AttributeData = attributes;
        frame.MaskData = mask;

        // Το sprite σημαδεύεται ως τροποποιημένο ώστε να ανέβει η χρονοσφραγίδα του.
        _context.Entry(sprite).Property(s => s.UpdatedUtc).IsModified = true;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(frame, sprite));
    }

    [HttpDelete("api/sprites/{id:long}/frames/{index:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteFrame(long id, int index, CancellationToken cancellationToken)
    {
        var sprite = await _access.FindWritableSpriteAsync(id, cancellationToken);

        if (sprite == null)
        {
            return NotFoundOrForbidden("Το sprite");
        }

        var frames = await _context.SpriteFrames
            .Where(f => f.SpriteId == id)
            .ToListAsync(cancellationToken);

        var frame = frames.SingleOrDefault(f => f.FrameIndex == index);

        if (frame == null)
        {
            return NotFoundOrForbidden("Το καρέ");
        }

        // Ένα sprite χωρίς κανένα καρέ δεν είναι sprite — θα άνοιγε άδειο στον editor.
        if (frames.Count == 1)
        {
            return Conflict(
                "Δεν γίνεται διαγραφή",
                "Το sprite πρέπει να έχει τουλάχιστον ένα καρέ. Διάγραψε το sprite αν δεν το θέλεις.");
        }

        _context.SpriteFrames.Remove(frame);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // --- Βοηθητικά -----------------------------------------------------------

    private ActionResult? ValidatePixels(string? base64, Sprite sprite, GraphicsMode mode, out byte[] pixels)
    {
        if (!TryDecodeBase64(base64, out pixels, out var error))
        {
            return InvalidRequest("Μη έγκυρα pixels", error!);
        }

        var expected = sprite.WidthPx * sprite.HeightPx;

        if (pixels.Length != expected)
        {
            return InvalidRequest(
                "Λάθος μέγεθος καρέ",
                "Το sprite είναι " + sprite.WidthPx + "×" + sprite.HeightPx + ", άρα χρειάζονται " +
                expected + " bytes· δόθηκαν " + pixels.Length + ".");
        }

        // Χωρίς αυτόν τον έλεγχο, μια τιμή εκτός ορίων θα περνούσε στη βάση και θα
        // έσκαγε αργότερα στο export — μακριά από την αιτία.
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] > mode.MaxPixelValue)
            {
                return InvalidRequest(
                    "Χρώμα εκτός ορίων",
                    "Το " + mode.Name + " επιτρέπει τιμές 0–" + mode.MaxPixelValue +
                    "· βρέθηκε " + pixels[i] + " στη θέση " + (i % sprite.WidthPx) + "," + (i / sprite.WidthPx) + ".");
            }
        }

        return null;
    }

    private static int ExpectedAttributeCount(Sprite sprite, GraphicsMode mode)
    {
        if (mode.ColorScope != ColorScope.PerCell || mode.CellWidth == 0 || mode.CellHeight == 0)
        {
            return 0;
        }

        var columns = (sprite.WidthPx + mode.CellWidth - 1) / mode.CellWidth;
        var rows = (sprite.HeightPx + mode.CellHeight - 1) / mode.CellHeight;

        return columns * rows;
    }

    private static SpriteFrameDto ToDto(SpriteFrame frame, Sprite sprite)
    {
        var pixels = RsprContainer.Read(frame.PixelData);
        var mask = frame.MaskData == null ? null : RsprContainer.Read(frame.MaskData);

        return new SpriteFrameDto(
            frame.FrameIndex,
            frame.DurationMs,
            sprite.WidthPx,
            sprite.HeightPx,
            Convert.ToBase64String(pixels.ToArray()),
            frame.AttributeData == null ? null : Convert.ToBase64String(frame.AttributeData),
            mask == null ? null : Convert.ToBase64String(mask.ToArray()));
    }
}
