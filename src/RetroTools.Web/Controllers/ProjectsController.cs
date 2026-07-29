using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Platforms;
using RetroTools.Data;
using RetroTools.Data.Entities;
using RetroTools.Web.Models;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : RetroApiController
{
    private readonly RetroToolsDbContext _context;
    private readonly ProjectAccess _access;

    public ProjectsController(RetroToolsDbContext context, ProjectAccess access)
    {
        _context = context;
        _access = access;
    }

    /// <summary>Τα projects του χρήστη. Με <c>scope=all</c> περιλαμβάνει και τα δημόσια.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(
        [FromQuery] string? scope,
        CancellationToken cancellationToken)
    {
        var userId = _access.RequireUserId();

        var query = _context.Projects.AsNoTracking();

        if (!string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.OwnerId == userId);
        }

        var projects = await query
            .OrderByDescending(p => p.UpdatedUtc)
            .Select(p => new { Project = p, SpriteCount = p.Sprites.Count })
            .ToListAsync(cancellationToken);

        return Ok(projects.Select(p => ProjectDto.From(p.Project, userId, p.SpriteCount)).ToList());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ProjectDto>> Get(long id, CancellationToken cancellationToken)
    {
        var project = await _access.FindReadableAsync(id, cancellationToken);

        if (project == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var spriteCount = await _context.Sprites.CountAsync(s => s.ProjectId == id, cancellationToken);

        return Ok(ProjectDto.From(project, _access.UserId, spriteCount));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ProjectDto>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!PlatformCatalog.TryGetMode(request.ModeCode, out var mode) || mode == null)
        {
            return InvalidRequest(
                "Άγνωστο mode",
                "Το mode '" + request.ModeCode + "' δεν υπάρχει. Δες /api/platforms.");
        }

        var platform = PlatformCatalog.Get(mode.Platform);

        if (request.PaletteProfileId != null
            && !platform.Palette.Profiles.Any(p => string.Equals(p.Id, request.PaletteProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            return InvalidRequest(
                "Άγνωστο palette profile",
                "Διαθέσιμα για " + platform.Code + ": " +
                string.Join(", ", platform.Palette.Profiles.Select(p => p.Id)) + ".");
        }

        var project = new Project
        {
            OwnerId = _access.RequireUserId(),
            Name = request.Name.Trim(),
            Description = request.Description,
            PlatformCode = platform.Code,
            ModeCode = mode.Code,
            PaletteProfileId = request.PaletteProfileId ?? platform.Palette.DefaultProfile.Id,
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = project.Id },
            ProjectDto.From(project, _access.UserId, 0));
    }

    [HttpPut("{id:long}")]
    [Authorize]
    public async Task<ActionResult<ProjectDto>> Update(
        long id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await _access.FindWritableAsync(id, cancellationToken);

        if (project == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        if (request.RowVersion.HasValue && request.RowVersion.Value != project.RowVersion)
        {
            return Conflict(
                "Το project άλλαξε στο μεταξύ",
                "Η έκδοσή σου είναι " + request.RowVersion.Value + ", η τρέχουσα " + project.RowVersion +
                ". Φόρτωσε ξανά πριν αποθηκεύσεις.");
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description;
        project.PaletteProfileId = request.PaletteProfileId;
        project.Visibility = request.Visibility;

        await _context.SaveChangesAsync(cancellationToken);

        var spriteCount = await _context.Sprites.CountAsync(s => s.ProjectId == id, cancellationToken);

        return Ok(ProjectDto.From(project, _access.UserId, spriteCount));
    }

    [HttpDelete("{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var project = await _access.FindWritableAsync(id, cancellationToken);

        if (project == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
