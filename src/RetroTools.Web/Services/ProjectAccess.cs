using Microsoft.EntityFrameworkCore;
using RetroTools.Data;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Services;

/// <summary>
/// Πρόσβαση σε projects με ρητό διαχωρισμό ανάγνωσης / εγγραφής.
/// </summary>
/// <remarks>
/// <b>Γιατί δεν αρκούν τα global query filters:</b> τα φίλτρα αφήνουν να φανούν και
/// τα δημόσια projects, που είναι σωστό για ανάγνωση. Αν όμως ένα endpoint εγγραφής
/// χρησιμοποιούσε το ίδιο ερώτημα, κάθε δημόσιο project θα γινόταν εγγράψιμο από
/// οποιονδήποτε. Κάθε διαδρομή που γράφει περνά υποχρεωτικά από
/// <see cref="FindWritableAsync"/>, που ελέγχει επιπλέον την ιδιοκτησία.
/// </remarks>
public sealed class ProjectAccess
{
    private readonly RetroToolsDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ProjectAccess(RetroToolsDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public Guid? UserId
    {
        get { return _currentUser.UserId; }
    }

    public Guid RequireUserId()
    {
        var id = _currentUser.UserId;

        if (id == null)
        {
            throw new InvalidOperationException("Το endpoint απαιτεί συνδεδεμένο χρήστη.");
        }

        return id.Value;
    }

    /// <summary>Ορατό project: δικό μου ή δημόσιο. Επιστρέφει null αν δεν υπάρχει ή δεν φαίνεται.</summary>
    public Task<Project?> FindReadableAsync(long projectId, CancellationToken cancellationToken = default)
    {
        return _context.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
    }

    /// <summary>Εγγράψιμο project: <b>μόνο</b> δικό μου.</summary>
    public Task<Project?> FindWritableAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        if (userId == null)
        {
            return Task.FromResult<Project?>(null);
        }

        return _context.Projects
            .SingleOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId.Value, cancellationToken);
    }

    public Task<Sprite?> FindReadableSpriteAsync(long spriteId, CancellationToken cancellationToken = default)
    {
        return _context.Sprites.SingleOrDefaultAsync(s => s.Id == spriteId, cancellationToken);
    }

    public Task<Sprite?> FindWritableSpriteAsync(long spriteId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        if (userId == null)
        {
            return Task.FromResult<Sprite?>(null);
        }

        return _context.Sprites
            .SingleOrDefaultAsync(s => s.Id == spriteId && s.Project!.OwnerId == userId.Value, cancellationToken);
    }

    public Task<SpriteMap?> FindReadableSpriteMapAsync(long id, CancellationToken cancellationToken = default)
    {
        return _context.SpriteMaps
            .Include(m => m.Cells)
            .SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public Task<SpriteMap?> FindWritableSpriteMapAsync(long id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        if (userId == null)
        {
            return Task.FromResult<SpriteMap?>(null);
        }

        return _context.SpriteMaps
            .Include(m => m.Cells)
            .SingleOrDefaultAsync(m => m.Id == id && m.Project!.OwnerId == userId.Value, cancellationToken);
    }
}
