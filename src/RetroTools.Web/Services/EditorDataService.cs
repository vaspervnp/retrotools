using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RetroTools.Data;

namespace RetroTools.Web.Services;

/// <summary>
/// Πρόσβαση στη βάση από Blazor components.
/// </summary>
/// <remarks>
/// <b>Γιατί δεν γίνεται απλό inject του DbContext:</b> στον Blazor Server το scope ζει
/// όσο το κύκλωμα — δηλαδή ώρες. Ένας DbContext τόσο μακρόβιος συσσωρεύει tracked
/// entities και δίνει μπαγιάτικα δεδομένα. Εδώ κάθε λειτουργία παίρνει δικό της context,
/// βραχύβιο, με ρητά δηλωμένο τον χρήστη.
/// <para>
/// Ο χρήστης δηλώνεται ρητά και όχι μέσω <c>IHttpContextAccessor</c>: στον Blazor Server
/// το <c>HttpContext</c> υπάρχει μόνο κατά την αρχική απόδοση και μετά είναι null.
/// Τα components παίρνουν την ταυτότητα από το <see cref="AuthenticationStateProvider"/>
/// και τη δίνουν εδώ.
/// </para>
/// </remarks>
public sealed class EditorDataService
{
    private readonly DbContextOptions<RetroToolsDbContext> _options;

    public EditorDataService(DbContextOptions<RetroToolsDbContext> options)
    {
        _options = options;
    }

    public RetroToolsDbContext CreateContext(Guid? userId)
    {
        return new RetroToolsDbContext(_options, new FixedCurrentUser(userId));
    }
}
