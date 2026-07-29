using System.Security.Claims;
using RetroTools.Data;

namespace RetroTools.Web.Configuration;

/// <summary>
/// Ο συνδεδεμένος χρήστης του τρέχοντος αιτήματος, από το authentication cookie.
/// Επιστρέφει <c>null</c> για ανώνυμους επισκέπτες — που τότε βλέπουν μόνο
/// δημόσια projects, χάρη στα φίλτρα του <see cref="RetroToolsDbContext"/>.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    /// <summary>Το claim που κρατά το δικό μας GUID, όχι το id του OAuth provider.</summary>
    public const string UserIdClaimType = "retrotools:uid";

    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? UserId
    {
        get
        {
            var principal = _accessor.HttpContext?.User;

            if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
            {
                return null;
            }

            var value = principal.FindFirstValue(UserIdClaimType);

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
