using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Data;
using RetroTools.Data.Entities;
using RetroTools.Web.Models;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

[ApiController]
public sealed class SpriteMapsController : RetroApiController
{
    private readonly RetroToolsDbContext _context;
    private readonly ProjectAccess _access;

    public SpriteMapsController(RetroToolsDbContext context, ProjectAccess access)
    {
        _context = context;
        _access = access;
    }

    [HttpGet("api/projects/{projectId:long}/spritemaps")]
    public async Task<ActionResult<IReadOnlyList<SpriteMapDto>>> List(
        long projectId,
        CancellationToken cancellationToken)
    {
        if (await _access.FindReadableAsync(projectId, cancellationToken) == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var maps = await _context.SpriteMaps
            .AsNoTracking()
            .Include(m => m.Cells)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);

        return Ok(maps.Select(SpriteMapDto.From).ToList());
    }

    [HttpGet("api/spritemaps/{id:long}")]
    public async Task<ActionResult<SpriteMapDto>> Get(long id, CancellationToken cancellationToken)
    {
        var map = await _access.FindReadableSpriteMapAsync(id, cancellationToken);

        if (map == null)
        {
            return NotFoundOrForbidden("Το spritemap");
        }

        return Ok(SpriteMapDto.From(map));
    }

    [HttpPost("api/projects/{projectId:long}/spritemaps")]
    [Authorize]
    public async Task<ActionResult<SpriteMapDto>> Create(
        long projectId,
        CreateSpriteMapRequest request,
        CancellationToken cancellationToken)
    {
        if (await _access.FindWritableAsync(projectId, cancellationToken) == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var map = new SpriteMap
        {
            ProjectId = projectId,
            Name = request.Name.Trim(),
            Columns = request.Columns,
            Rows = request.Rows,
            CellWidthPx = request.CellWidthPx,
            CellHeightPx = request.CellHeightPx,
        };

        _context.SpriteMaps.Add(map);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = map.Id }, SpriteMapDto.From(map));
    }

    [HttpPut("api/spritemaps/{id:long}")]
    [Authorize]
    public async Task<ActionResult<SpriteMapDto>> Update(
        long id,
        UpdateSpriteMapRequest request,
        CancellationToken cancellationToken)
    {
        var map = await _access.FindWritableSpriteMapAsync(id, cancellationToken);

        if (map == null)
        {
            return NotFoundOrForbidden("Το spritemap");
        }

        if (request.RowVersion.HasValue && request.RowVersion.Value != map.RowVersion)
        {
            return Conflict(
                "Το spritemap άλλαξε στο μεταξύ",
                "Η έκδοσή σου είναι " + request.RowVersion.Value + ", η τρέχουσα " + map.RowVersion + ".");
        }

        var filled = request.Cells.Where(c => c.SpriteId.HasValue).ToList();

        foreach (var cell in filled)
        {
            if (cell.Column < 0 || cell.Column >= request.Columns
                || cell.Row < 0 || cell.Row >= request.Rows)
            {
                return InvalidRequest(
                    "Κελί εκτός πλέγματος",
                    "Το κελί " + cell.Column + "," + cell.Row + " δεν χωράει σε πλέγμα " +
                    request.Columns + "×" + request.Rows + ".");
            }
        }

        if (filled.Select(c => (c.Column, c.Row)).Distinct().Count() != filled.Count)
        {
            return InvalidRequest("Διπλό κελί", "Δύο εγγραφές δείχνουν στην ίδια θέση του πλέγματος.");
        }

        // Τα sprites πρέπει να ανήκουν στο ίδιο project — αλλιώς ένα spritemap θα
        // μπορούσε να δείχνει σε ξένο sprite και να το εκθέτει έμμεσα.
        var spriteIds = filled.Select(c => c.SpriteId!.Value).Distinct().ToList();

        if (spriteIds.Count > 0)
        {
            var valid = await _context.Sprites
                .Where(s => spriteIds.Contains(s.Id) && s.ProjectId == map.ProjectId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var missing = spriteIds.Except(valid).ToList();

            if (missing.Count > 0)
            {
                return InvalidRequest(
                    "Άγνωστο sprite",
                    "Τα sprites " + string.Join(", ", missing) + " δεν ανήκουν σε αυτό το project.");
            }
        }

        map.Name = request.Name.Trim();
        map.Columns = request.Columns;
        map.Rows = request.Rows;
        map.CellWidthPx = request.CellWidthPx;
        map.CellHeightPx = request.CellHeightPx;

        _context.SpriteMapCells.RemoveRange(map.Cells);

        foreach (var cell in filled)
        {
            var flags = SpriteMapCellFlags.None;

            if (cell.FlipHorizontal)
            {
                flags |= SpriteMapCellFlags.FlipHorizontal;
            }

            if (cell.FlipVertical)
            {
                flags |= SpriteMapCellFlags.FlipVertical;
            }

            _context.SpriteMapCells.Add(new SpriteMapCell
            {
                SpriteMapId = map.Id,
                Column = cell.Column,
                Row = cell.Row,
                SpriteId = cell.SpriteId,
                FrameIndex = cell.FrameIndex,
                Flags = flags,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _access.FindWritableSpriteMapAsync(id, cancellationToken);

        return Ok(SpriteMapDto.From(reloaded!));
    }

    [HttpDelete("api/spritemaps/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var map = await _access.FindWritableSpriteMapAsync(id, cancellationToken);

        if (map == null)
        {
            return NotFoundOrForbidden("Το spritemap");
        }

        _context.SpriteMaps.Remove(map);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
