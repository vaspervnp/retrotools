using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroTools.Core.Serialization;
using RetroTools.Web.Services;

namespace RetroTools.Web.Controllers;

/// <summary>Εξαγωγή και εισαγωγή ολόκληρου project ως JSON.</summary>
[ApiController]
[Route("api/projects")]
public sealed class ProjectDocumentController : RetroApiController
{
    /// <summary>
    /// Όριο μεγέθους ανεβασμένου αρχείου. Το επιβάλλουμε <b>πριν</b> διαβάσουμε το
    /// σώμα: χωρίς αυτό, ένα αρχείο πολλών GB θα καταναλωνόταν ολόκληρο στη μνήμη
    /// πριν καν φτάσουμε στην επικύρωση.
    /// </summary>
    private const int MaxUploadBytes = 32 * 1024 * 1024;

    private readonly ProjectDocumentService _documents;
    private readonly ProjectAccess _access;

    public ProjectDocumentController(ProjectDocumentService documents, ProjectAccess access)
    {
        _documents = documents;
        _access = access;
    }

    [HttpGet("{id:long}/document")]
    public async Task<IActionResult> Export(long id, CancellationToken cancellationToken)
    {
        var document = await _documents.ExportAsync(id, cancellationToken);

        if (document == null)
        {
            return NotFoundOrForbidden("Το project");
        }

        var bytes = ProjectDocumentSerializer.Write(document);
        var fileName = MakeFileName(document.Name);

        return File(bytes, "application/json; charset=utf-8", fileName);
    }

    [HttpPost("import")]
    [Authorize]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Import(
        IFormFile? file,
        [FromQuery] string? name,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return InvalidRequest("Λείπει το αρχείο", "Ανέβασε ένα αρχείο .json εξαγωγής project.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return InvalidRequest(
                "Πολύ μεγάλο αρχείο",
                "Το όριο είναι " + (MaxUploadBytes / (1024 * 1024)) + " MB.");
        }

        byte[] content;

        await using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream, cancellationToken);
            content = stream.ToArray();
        }

        var read = ProjectDocumentSerializer.Read(content);

        if (!read.Success)
        {
            // Όλα τα σφάλματα μαζί: ο χρήστης διορθώνει μία φορά, όχι επτά.
            return Problem(
                title: "Το αρχείο δεν είναι έγκυρο",
                detail: string.Join(" ", read.Errors),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var project = await _documents.ImportAsync(
            read.Document!,
            _access.RequireUserId(),
            name,
            cancellationToken);

        return Ok(new
        {
            id = project.Id,
            name = project.Name,
            platformCode = project.PlatformCode,
            modeCode = project.ModeCode,
            sprites = read.Document!.Sprites.Count,
            spriteMaps = read.Document.SpriteMaps.Count,
            groups = read.Document.Groups.Count,
        });
    }

    /// <summary>
    /// Καθαρίζει το όνομα για χρήση ως filename. Τα ελληνικά διατηρούνται —
    /// αφαιρούνται μόνο οι χαρακτήρες που απαγορεύονται σε διαδρομές.
    /// </summary>
    private static string MakeFileName(string projectName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(projectName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        if (cleaned.Length == 0)
        {
            cleaned = "project";
        }

        return cleaned + ".retrotools.json";
    }
}
