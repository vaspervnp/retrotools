using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetroTools.Data;
using RetroTools.Data.Entities;

namespace RetroTools.Data.Tests;

/// <summary>
/// Μοιραζόμενο περιβάλλον για τα integration tests. Τρέχουν πάνω στην <b>πραγματική</b>
/// MariaDB — ένα in-memory provider δεν θα αποκάλυπτε ποτέ προβλήματα με BLOB,
/// collation ή τη μετάφραση των query filters σε SQL.
/// </summary>
/// <remarks>
/// Κάθε test δουλεύει με δικούς του χρήστες (τυχαία GUID). Στο τέλος διαγράφονται,
/// και μαζί τους — μέσω <c>ON DELETE CASCADE</c> — όλα τα projects, sprites και
/// spritemaps τους. Έτσι δεν χρειάζεται ξεχωριστή βάση για τα tests.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly List<Guid> _createdUsers = new List<Guid>();

    public DbContextOptions<RetroToolsDbContext>? Options { get; private set; }

    public bool IsAvailable
    {
        get { return Options != null; }
    }

    public async Task InitializeAsync()
    {
        var connectionString = TestConfiguration.ConnectionString;

        if (connectionString == null)
        {
            return;
        }

        Options = new DbContextOptionsBuilder<RetroToolsDbContext>()
            .UseMySql(connectionString, DependencyInjection.MariaDb11)
            .ConfigureWarnings(w =>
                w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .EnableSensitiveDataLogging()
            .Options;

        // Οι πίνακες πλατφορμών είναι υποδομή, όχι δεδομένα δοκιμής: κάθε project
        // έχει ξένο κλειδί προς αυτούς. Χωρίς seeding εδώ, η σειρά εκτέλεσης των
        // κλάσεων θα καθόριζε ποια tests περνούν.
        await using var context = CreateSystemContext();
        await Seeding.PlatformSeeder.SeedAsync(context);
    }

    /// <summary>Context που «βλέπει» ως ο συγκεκριμένος χρήστης — με ενεργά τα φίλτρα.</summary>
    public RetroToolsDbContext CreateContext(Guid? userId)
    {
        return new RetroToolsDbContext(RequireOptions(), new FixedCurrentUser(userId));
    }

    /// <summary>Context συστήματος — παρακάμπτει τα φίλτρα. Μόνο για setup/teardown.</summary>
    public RetroToolsDbContext CreateSystemContext()
    {
        return new RetroToolsDbContext(RequireOptions(), SystemUser.Instance);
    }

    /// <summary>
    /// Καταγράφει χρήστη που δημιουργήθηκε από τον υπό δοκιμή κώδικα (π.χ. από το
    /// <c>UserProvisioningService</c>), ώστε να καθαριστεί κι αυτός στο τέλος.
    /// </summary>
    public void Track(Guid userId)
    {
        lock (_createdUsers)
        {
            _createdUsers.Add(userId);
        }
    }

    public async Task<User> CreateUserAsync(string displayName)
    {
        await using var context = CreateSystemContext();

        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        lock (_createdUsers)
        {
            _createdUsers.Add(user.Id);
        }

        return user;
    }

    public async Task DisposeAsync()
    {
        if (Options == null)
        {
            return;
        }

        await using var context = CreateSystemContext();

        Guid[] ids;
        lock (_createdUsers)
        {
            ids = _createdUsers.ToArray();
            _createdUsers.Clear();
        }

        if (ids.Length == 0)
        {
            return;
        }

        await context.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
    }

    private DbContextOptions<RetroToolsDbContext> RequireOptions()
    {
        if (Options == null)
        {
            throw new InvalidOperationException(
                "Δεν έχει ρυθμιστεί connection string — τα integration tests έπρεπε να είχαν γίνει skip.");
        }

        return Options;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
