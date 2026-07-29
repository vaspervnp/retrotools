using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroTools.Data;

namespace RetroTools.Web.Controllers;

[ApiController]
[Route("api/me")]
public sealed class MeController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly RetroToolsDbContext _context;

    public MeController(ICurrentUser currentUser, RetroToolsDbContext context)
    {
        _currentUser = currentUser;
        _context = context;
    }

    /// <summary>
    /// Ο τρέχων χρήστης. Επιστρέφει 200 με <c>authenticated: false</c> αντί για 401,
    /// ώστε το UI να μπορεί να ρωτήσει χωρίς να χειρίζεται σφάλμα.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        if (userId == null)
        {
            return Ok(new { authenticated = false });
        }

        var user = await _context.Users
            .Include(u => u.Logins)
            .SingleOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user == null)
        {
            // Το cookie δείχνει σε λογαριασμό που δεν υπάρχει πια (π.χ. διαγράφηκε).
            return Ok(new { authenticated = false });
        }

        return Ok(new
        {
            authenticated = true,
            id = user.Id,
            displayName = user.DisplayName,
            email = user.Email,
            avatarUrl = user.AvatarUrl,
            providers = user.Logins.Select(l => l.Provider).OrderBy(p => p).ToArray(),
        });
    }
}
