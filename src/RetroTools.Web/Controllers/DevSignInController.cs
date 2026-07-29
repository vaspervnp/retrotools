using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Data;
using RetroTools.Data.Entities;
using RetroTools.Web.Configuration;

namespace RetroTools.Web.Controllers;

/// <summary>
/// Σύνδεση ως τοπικός δοκιμαστικός χρήστης, <b>χωρίς</b> OAuth.
/// </summary>
/// <remarks>
/// <para>
/// Υπάρχει επειδή αλλιώς είναι αδύνατο να δουλέψει κανείς στο UI χωρίς να έχει
/// στήσει OAuth apps σε GitHub και Google.
/// </para>
/// <para>
/// <b>Διπλή ασφάλεια:</b> ο controller απαντά μόνο όταν το περιβάλλον είναι
/// Development <b>και</b> η ρύθμιση <c>RetroTools:EnableDevSignIn</c> είναι ρητά
/// <c>true</c>. Και τα δύο μαζί. Σε οποιαδήποτε άλλη περίπτωση επιστρέφει 404,
/// σαν να μην υπάρχει η διαδρομή.
/// </para>
/// </remarks>
[Route("account/dev")]
public sealed class DevSignInController : Controller
{
    /// <summary>Σταθερό id ώστε τα δεδομένα να επιβιώνουν ανάμεσα σε συνεδρίες ανάπτυξης.</summary>
    private static readonly Guid DevUserId = new Guid("d0000000-0000-4000-8000-000000000001");

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly RetroToolsDbContext _context;

    public DevSignInController(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        RetroToolsDbContext context)
    {
        _environment = environment;
        _configuration = configuration;
        _context = context;
    }

    private bool IsEnabled
    {
        get
        {
            return _environment.IsDevelopment()
                   && _configuration.GetValue<bool>("RetroTools:EnableDevSignIn");
        }
    }

    [HttpGet("signin")]
    public async Task<IActionResult> SignInAsDeveloper([FromQuery] string? returnUrl)
    {
        if (!IsEnabled)
        {
            return NotFound();
        }

        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == DevUserId);

        if (user == null)
        {
            user = new User
            {
                Id = DevUserId,
                DisplayName = "Τοπικός δοκιμαστής",
                Email = "dev@localhost",
            };

            _context.Users.Add(user);
            _context.UserLogins.Add(new UserLogin
            {
                Provider = "dev",
                ProviderKey = DevUserId.ToString(),
                UserId = DevUserId,
            });

            await _context.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(HttpContextCurrentUser.UserIdClaimType, DevUserId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));

        await HttpContext.SignInAsync(
            AuthenticationExtensions.CookieScheme,
            new ClaimsPrincipal(identity));

        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/projects");
    }
}
