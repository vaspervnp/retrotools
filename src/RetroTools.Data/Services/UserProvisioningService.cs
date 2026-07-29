using Microsoft.EntityFrameworkCore;
using RetroTools.Data.Entities;

namespace RetroTools.Data.Services;

/// <summary>Τι έγινε όταν κάποιος συνδέθηκε με εξωτερικό provider.</summary>
public enum UserProvisioningOutcome
{
    /// <summary>Υπήρχε ήδη σύνδεση με αυτόν τον provider.</summary>
    SignedIn = 0,

    /// <summary>Δημιουργήθηκε νέος λογαριασμός.</summary>
    Created = 1,

    /// <summary>
    /// Το email ανήκει σε υπάρχοντα λογαριασμό που συνδέεται με άλλον provider.
    /// Δεν γίνεται αυτόματη σύνδεση — βλ. σχόλιο στο <see cref="SignInAsync"/>.
    /// </summary>
    EmailBelongsToAnotherAccount = 2,
}

public sealed record ExternalLoginInfo(
    string Provider,
    string ProviderKey,
    string DisplayName,
    string? Email,
    string? AvatarUrl);

public sealed record UserProvisioningResult(
    UserProvisioningOutcome Outcome,
    User? User,
    IReadOnlyList<string> ExistingProviders);

/// <summary>
/// Μετατρέπει μια εξωτερική ταυτότητα (GitHub / Google) σε λογαριασμό RetroTools.
/// </summary>
public sealed class UserProvisioningService
{
    private readonly RetroToolsDbContext _context;

    public UserProvisioningService(RetroToolsDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Βρίσκει ή δημιουργεί τον λογαριασμό για μια εξωτερική σύνδεση.
    /// </summary>
    /// <remarks>
    /// <b>Δεν γίνεται αυτόματη σύνδεση λογαριασμών βάσει email.</b> Θα ήταν βολικό,
    /// αλλά είναι γνωστός δρόμος κατάληψης λογαριασμού: αν ένας provider επιστρέψει
    /// ανεπιβεβαίωτο email, οποιοσδήποτε μπορεί να δηλώσει το email του θύματος και
    /// να αποκτήσει πρόσβαση στα projects του. Αντ' αυτού ο χρήστης ενημερώνεται να
    /// συνδεθεί με τον αρχικό provider και να δέσει τον δεύτερο από τις ρυθμίσεις,
    /// όπου η ταυτότητά του είναι ήδη αποδεδειγμένη.
    /// </remarks>
    public async Task<UserProvisioningResult> SignInAsync(
        ExternalLoginInfo info,
        CancellationToken cancellationToken = default)
    {
        if (info == null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        var now = DateTime.UtcNow;

        var login = await _context.UserLogins
            .Include(l => l.User)
            .SingleOrDefaultAsync(
                l => l.Provider == info.Provider && l.ProviderKey == info.ProviderKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (login?.User != null)
        {
            var existing = login.User;
            existing.LastLoginUtc = now;
            UpdateProfile(existing, info);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new UserProvisioningResult(
                UserProvisioningOutcome.SignedIn,
                existing,
                Array.Empty<string>());
        }

        if (!string.IsNullOrWhiteSpace(info.Email))
        {
            var byEmail = await _context.Users
                .Include(u => u.Logins)
                .FirstOrDefaultAsync(u => u.Email == info.Email, cancellationToken)
                .ConfigureAwait(false);

            if (byEmail != null)
            {
                return new UserProvisioningResult(
                    UserProvisioningOutcome.EmailBelongsToAnotherAccount,
                    null,
                    byEmail.Logins.Select(l => l.Provider).Distinct().ToList());
            }
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? info.Provider + " user" : info.DisplayName,
            Email = info.Email,
            AvatarUrl = info.AvatarUrl,
            LastLoginUtc = now,
        };

        user.Logins.Add(new UserLogin
        {
            Provider = info.Provider,
            ProviderKey = info.ProviderKey,
        });

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new UserProvisioningResult(UserProvisioningOutcome.Created, user, Array.Empty<string>());
    }

    /// <summary>
    /// Δένει έναν δεύτερο provider σε λογαριασμό που είναι <b>ήδη συνδεδεμένος</b>.
    /// Εδώ η ταυτότητα είναι αποδεδειγμένη, οπότε το δέσιμο είναι ασφαλές.
    /// </summary>
    /// <returns><c>false</c> αν η εξωτερική ταυτότητα ανήκει ήδη σε άλλον λογαριασμό.</returns>
    public async Task<bool> LinkAsync(
        Guid userId,
        ExternalLoginInfo info,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserLogins
            .SingleOrDefaultAsync(
                l => l.Provider == info.Provider && l.ProviderKey == info.ProviderKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return existing.UserId == userId;
        }

        _context.UserLogins.Add(new UserLogin
        {
            Provider = info.Provider,
            ProviderKey = info.ProviderKey,
            UserId = userId,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> UnlinkAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var logins = await _context.UserLogins
            .Where(l => l.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ο τελευταίος provider δεν αφαιρείται: θα έμενε λογαριασμός χωρίς τρόπο σύνδεσης.
        if (logins.Count <= 1)
        {
            return false;
        }

        var target = logins.SingleOrDefault(l => l.Provider == provider);

        if (target == null)
        {
            return false;
        }

        _context.UserLogins.Remove(target);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static void UpdateProfile(User user, ExternalLoginInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.DisplayName))
        {
            user.DisplayName = info.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(info.AvatarUrl))
        {
            user.AvatarUrl = info.AvatarUrl;
        }

        // Το email δεν αντικαθίσταται αν υπάρχει ήδη — αλλιώς μια αλλαγή στον
        // provider θα μπορούσε να δημιουργήσει σύγκρουση με άλλον λογαριασμό.
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            user.Email = info.Email;
        }
    }
}
