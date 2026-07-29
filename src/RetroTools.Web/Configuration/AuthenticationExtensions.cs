using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using RetroTools.Data.Entities;
using RetroTools.Data.Services;

namespace RetroTools.Web.Configuration;

public static class AuthenticationExtensions
{
    public const string CookieScheme = "RetroTools.Cookie";

    /// <summary>
    /// Δικοί μας τύποι claim για το avatar. Κανένας από τους δύο providers δεν
    /// χαρτογραφεί την εικόνα προφίλ από προεπιλογή, οπότε τη δηλώνουμε εμείς.
    /// </summary>
    private const string GitHubAvatarClaim = "urn:github:avatar";

    private const string GoogleAvatarClaim = "urn:google:picture";

    /// <summary>
    /// Cookie authentication με GitHub και Google. <b>Χωρίς ASP.NET Core Identity</b>:
    /// δεν υπάρχουν τοπικοί κωδικοί, οπότε το μόνο που χρειάζεται είναι η αντιστοίχιση
    /// εξωτερικής ταυτότητας → δικός μας λογαριασμός.
    /// </summary>
    public static AuthenticationSettings AddRetroToolsAuthentication(
        this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration
            .GetSection(AuthenticationSettings.SectionName)
            .Get<AuthenticationSettings>() ?? new AuthenticationSettings();

        builder.Services.AddScoped<UserProvisioningService>();

        var authentication = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "retrotools.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                options.LoginPath = "/account/signin";
                options.LogoutPath = "/account/signout";
                options.AccessDeniedPath = "/account/denied";

                // Οι σελίδες ανακατευθύνονται στη σύνδεση· τα API endpoints όχι.
                // Ένα 302 προς HTML σελίδα σε απάντηση fetch() είναι άχρηστο για τον
                // client — θέλει καθαρό 401/403 για να αντιδράσει.
                options.Events.OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        if (settings.GitHub.IsConfigured)
        {
            authentication.AddGitHub(options =>
            {
                options.ClientId = settings.GitHub.ClientId;
                options.ClientSecret = settings.GitHub.ClientSecret;
                options.SignInScheme = CookieScheme;
                options.CallbackPath = "/signin-github";

                // Χρειάζεται για να επιστρέψει το GitHub το email του χρήστη.
                options.Scope.Add("user:email");

                // Ο provider δεν χαρτογραφεί το avatar από μόνος του.
                options.ClaimActions.MapJsonKey(GitHubAvatarClaim, "avatar_url");

                options.Events.OnTicketReceived = context =>
                    ProvisionAsync(context, UserLogin.GitHub);
            });
        }

        if (settings.Google.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = settings.Google.ClientId;
                options.ClientSecret = settings.Google.ClientSecret;
                options.SignInScheme = CookieScheme;
                options.CallbackPath = "/signin-google";

                // Ούτε ο Google χαρτογραφεί το picture από προεπιλογή.
                options.ClaimActions.MapJsonKey(GoogleAvatarClaim, "picture");

                options.Events.OnTicketReceived = context =>
                    ProvisionAsync(context, UserLogin.Google);
            });
        }

        builder.Services.AddAuthorization();

        return settings;
    }

    /// <summary>
    /// Τρέχει μετά την ταυτοποίηση από τον provider και <b>πριν</b> εκδοθεί το cookie.
    /// Η χρονική σειρά έχει σημασία: αν το email συγκρούεται με υπάρχοντα λογαριασμό,
    /// πρέπει να ματαιώσουμε τη σύνδεση εντελώς. Αν το κάναμε αργότερα, ο χρήστης θα
    /// έμενε με έγκυρο cookie χωρίς αντίστοιχο λογαριασμό — συνδεδεμένος αλλά αόρατος
    /// στα φίλτρα, βλέποντας μια άδεια εφαρμογή χωρίς εξήγηση.
    /// </summary>
    private static async Task ProvisionAsync(TicketReceivedContext context, string provider)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var providerKey = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new InvalidOperationException(
                "Ο provider '" + provider + "' δεν επέστρεψε σταθερό αναγνωριστικό χρήστη.");
        }

        var info = new ExternalLoginInfo(
            provider,
            providerKey,
            identity.FindFirst(ClaimTypes.Name)?.Value ?? providerKey,
            identity.FindFirst(ClaimTypes.Email)?.Value,
            ReadAvatarUrl(identity, provider));

        var provisioning = context.HttpContext.RequestServices.GetRequiredService<UserProvisioningService>();
        var result = await provisioning.SignInAsync(info, context.HttpContext.RequestAborted);

        if (result.Outcome == UserProvisioningOutcome.EmailBelongsToAnotherAccount)
        {
            // HandleResponse() σταματά τον handler πριν το SignInAsync — δεν εκδίδεται cookie.
            context.HandleResponse();
            context.Response.Redirect(
                "/account/link-required?providers=" +
                Uri.EscapeDataString(string.Join(",", result.ExistingProviders)) +
                "&attempted=" + Uri.EscapeDataString(provider));
            return;
        }

        identity.AddClaim(new Claim(
            HttpContextCurrentUser.UserIdClaimType,
            result.User!.Id.ToString()));
    }

    private static bool IsApiRequest(HttpRequest request)
    {
        return request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadAvatarUrl(ClaimsIdentity identity, string provider)
    {
        return provider == UserLogin.GitHub
            ? identity.FindFirst(GitHubAvatarClaim)?.Value
            : identity.FindFirst(GoogleAvatarClaim)?.Value;
    }
}
