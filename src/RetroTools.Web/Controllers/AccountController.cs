using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using RetroTools.Data.Entities;
using RetroTools.Web.Configuration;

namespace RetroTools.Web.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    private readonly AuthenticationSettings _settings;

    public AccountController(AuthenticationSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Ποιοι providers είναι διαθέσιμοι — το UI δείχνει μόνο αυτούς.</summary>
    [HttpGet("providers")]
    public IActionResult Providers()
    {
        return Ok(new
        {
            github = _settings.GitHub.IsConfigured,
            google = _settings.Google.IsConfigured,
        });
    }

    [HttpGet("signin")]
    public IActionResult SignIn([FromQuery] string? provider, [FromQuery] string? returnUrl)
    {
        var scheme = ResolveScheme(provider);

        if (scheme == null)
        {
            return Problem(
                title: "Μη διαθέσιμος provider",
                detail: "Ο provider '" + provider + "' δεν είναι ρυθμισμένος. " +
                        "Διαθέσιμοι: " + string.Join(", ", AvailableProviders()) + ".",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Ο returnUrl ελέγχεται ότι είναι τοπικός — αλλιώς είναι open redirect,
        // δηλαδή έτοιμο εργαλείο phishing με το domain μας ως δόλωμα.
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";

        return Challenge(new AuthenticationProperties { RedirectUri = target }, scheme);
    }

    /// <summary>
    /// Αποσύνδεση μόνο με POST: ένα GET θα μπορούσε να ενεργοποιηθεί από
    /// <c>&lt;img src&gt;</c> σε ξένη σελίδα και να πετάει τον χρήστη έξω.
    /// </summary>
    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public new async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(AuthenticationExtensions.CookieScheme);
        return Redirect("/");
    }

    [HttpGet("link-required")]
    public IActionResult LinkRequired([FromQuery] string? providers, [FromQuery] string? attempted)
    {
        var existing = string.IsNullOrWhiteSpace(providers) ? "άλλον provider" : providers;

        return Problem(
            title: "Υπάρχει ήδη λογαριασμός με αυτό το email",
            detail: "Το email σου είναι ήδη δεμένο με λογαριασμό που συνδέεται μέσω " + existing + ". " +
                    "Συνδέσου με αυτόν και μετά δέσε τον '" + attempted + "' από τις ρυθμίσεις του λογαριασμού. " +
                    "Δεν συνδέουμε αυτόματα λογαριασμούς βάσει email για λόγους ασφαλείας.",
            statusCode: StatusCodes.Status409Conflict);
    }

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        return Problem(
            title: "Δεν επιτρέπεται η πρόσβαση",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private string? ResolveScheme(string? provider)
    {
        if (string.Equals(provider, UserLogin.GitHub, StringComparison.OrdinalIgnoreCase)
            && _settings.GitHub.IsConfigured)
        {
            return GitHubAuthenticationDefaults.AuthenticationScheme;
        }

        if (string.Equals(provider, UserLogin.Google, StringComparison.OrdinalIgnoreCase)
            && _settings.Google.IsConfigured)
        {
            return GoogleDefaults.AuthenticationScheme;
        }

        return null;
    }

    private IEnumerable<string> AvailableProviders()
    {
        if (_settings.GitHub.IsConfigured)
        {
            yield return UserLogin.GitHub;
        }

        if (_settings.Google.IsConfigured)
        {
            yield return UserLogin.Google;
        }
    }
}
