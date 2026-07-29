using Microsoft.AspNetCore.Mvc;
using RetroTools.Core.Platforms;
using RetroTools.Web.Models;

namespace RetroTools.Web.Controllers;

/// <summary>
/// Τα δεδομένα υλικού. Δημόσιο και αμετάβλητο — δεν απαιτεί σύνδεση και μπορεί
/// να μείνει στην cache του browser.
/// </summary>
[ApiController]
[Route("api/platforms")]
public sealed class PlatformsController : ControllerBase
{
    private static readonly IReadOnlyList<PlatformDto> Catalog =
        PlatformCatalog.All.Select(PlatformDto.From).ToList();

    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public ActionResult<IReadOnlyList<PlatformDto>> GetAll()
    {
        return Ok(Catalog);
    }

    [HttpGet("{code}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public ActionResult<PlatformDto> Get(string code)
    {
        var platform = Catalog.SingleOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

        if (platform == null)
        {
            return Problem(
                title: "Άγνωστη πλατφόρμα",
                detail: "Διαθέσιμες: " + string.Join(", ", Catalog.Select(p => p.Code)) + ".",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(platform);
    }

    [HttpGet("modes/{modeCode}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public ActionResult<GraphicsModeDto> GetMode(string modeCode)
    {
        if (!PlatformCatalog.TryGetMode(modeCode, out var mode) || mode == null)
        {
            return Problem(
                title: "Άγνωστο mode",
                detail: "Το mode '" + modeCode + "' δεν υπάρχει.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(GraphicsModeDto.From(mode));
    }
}
