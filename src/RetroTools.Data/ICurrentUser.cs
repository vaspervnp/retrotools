namespace RetroTools.Data;

/// <summary>
/// Ποιος κάνει το τρέχον αίτημα. Το <see cref="RetroToolsDbContext"/> το χρησιμοποιεί
/// για να φιλτράρει αυτόματα τα δεδομένα άλλων χρηστών.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Null όταν ο χρήστης δεν είναι συνδεδεμένος.</summary>
    Guid? UserId { get; }
}

/// <summary>
/// Χρήστης «σύστημα»: παρακάμπτει τα φίλτρα ιδιοκτησίας.
/// Χρησιμοποιείται μόνο για migrations, seeding και εργασίες συντήρησης —
/// <b>ποτέ</b> σε δρόμο κώδικα που εξυπηρετεί αίτημα χρήστη.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new SystemUser();

    public Guid? UserId
    {
        get { return null; }
    }

    public bool BypassOwnershipFilters
    {
        get { return true; }
    }
}

/// <summary>Σταθερός χρήστης — για tests και background jobs που τρέχουν εκ μέρους κάποιου.</summary>
public sealed class FixedCurrentUser : ICurrentUser
{
    public FixedCurrentUser(Guid? userId)
    {
        UserId = userId;
    }

    public Guid? UserId { get; }
}
