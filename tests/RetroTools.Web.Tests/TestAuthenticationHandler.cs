using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetroTools.Web.Configuration;

namespace RetroTools.Web.Tests;

/// <summary>
/// Ταυτοποίηση για τα tests: το αίτημα δηλώνει ποιος είναι με ένα header.
/// </summary>
/// <remarks>
/// Παράγει <b>το ίδιο claim</b> (<c>retrotools:uid</c>) με την πραγματική ροή OAuth,
/// οπότε ο υπόλοιπος κώδικας — <c>[Authorize]</c>, <c>ICurrentUser</c>, τα φίλτρα
/// ιδιοκτησίας του DbContext — δοκιμάζεται ακριβώς όπως τρέχει στην παραγωγή.
/// Αίτημα χωρίς το header είναι ανώνυμο, όπως ένας μη συνδεδεμένος επισκέπτης.
/// </remarks>
public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public const string UserHeader = "X-Test-User";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(values.ToString(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Το header " + UserHeader + " δεν είναι GUID."));
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(HttpContextCurrentUser.UserIdClaimType, userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, "test-" + userId.ToString("N").Substring(0, 6)));

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
