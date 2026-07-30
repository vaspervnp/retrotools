using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using RetroTools.Data;

namespace RetroTools.Migrator;

public sealed record MigrationStatus(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending,
    IReadOnlyList<string> All)
{
    public bool UpToDate
    {
        get { return Pending.Count == 0; }
    }

    /// <summary>
    /// Migrations που έχει η βάση αλλά δεν γνωρίζει αυτό το εκτελέσιμο. Σημαίνει ότι
    /// η βάση είναι <b>νεότερη</b> από το εργαλείο — τυπικά μισοτελειωμένο rollback ή
    /// λάθος έκδοση αρχείου. Το να συνεχίσουμε σιωπηλά θα ήταν επικίνδυνο.
    /// </summary>
    public IReadOnlyList<string> Unknown
    {
        get { return Applied.Except(All, StringComparer.Ordinal).ToList(); }
    }
}

/// <summary>
/// Εφαρμογή EF Core migrations χωρίς <c>dotnet ef</c>.
/// </summary>
/// <remarks>
/// Τα migrations είναι μεταγλωττισμένα μέσα στο <c>RetroTools.Data</c> — το
/// <c>dotnet ef</c> χρειάζεται μόνο για να τα <i>δημιουργήσει</i>. Για να τα
/// <i>εφαρμόσει</i> αρκεί το EF Core runtime, που αυτό το εργαλείο κουβαλά μαζί του.
/// </remarks>
public sealed class MigrationRunner : IAsyncDisposable
{
    private readonly RetroToolsDbContext _context;
    private readonly string _serverOnlyConnectionString;

    public MigrationRunner(string connectionString, int commandTimeoutSeconds)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        Host = builder.Server;
        DatabaseName = builder.Database;

        if (string.IsNullOrWhiteSpace(DatabaseName))
        {
            throw new ArgumentException(
                "Το connection string δεν δηλώνει βάση (Database=...).",
                nameof(connectionString));
        }

        // Το όνομα θα παρεμβληθεί σε DDL, όπου δεν επιτρέπονται παράμετροι.
        // Ο περιορισμός είναι αυστηρότερος από ό,τι δέχεται η MariaDB, επίτηδες.
        if (!DatabaseName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                "Το όνομα βάσης '" + DatabaseName + "' επιτρέπεται να έχει μόνο γράμματα, ψηφία και _.",
                nameof(connectionString));
        }

        var serverOnly = new MySqlConnectionStringBuilder(connectionString);
        serverOnly.Database = string.Empty;
        _serverOnlyConnectionString = serverOnly.ConnectionString;

        var options = new DbContextOptionsBuilder<RetroToolsDbContext>()
            .UseMySql(connectionString, DependencyInjection.MariaDb11, mysql =>
            {
                // Ένα migration σε μεγάλο πίνακα μπορεί να αργήσει· το προεπιλεγμένο
                // timeout των 30 δευτερολέπτων θα το έκοβε στη μέση.
                mysql.CommandTimeout(commandTimeoutSeconds);
            })
            .ConfigureWarnings(w =>
                w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;

        // Τα migrations δεν ανήκουν σε χρήστη — παρακάμπτουν τα φίλτρα ιδιοκτησίας.
        _context = new RetroToolsDbContext(options, SystemUser.Instance);
    }

    public string Host { get; }

    public string DatabaseName { get; }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return _context.Database.CanConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Συνδέεται στον <b>διακομιστή</b> χωρίς να δηλώσει βάση.
    /// </summary>
    /// <remarks>
    /// Ξεχωρίζει το «λάθος κωδικός ή κλειστό firewall» από το «η βάση δεν υπάρχει
    /// ακόμη» — δύο καταστάσεις με εντελώς διαφορετική λύση, που ένα σκέτο
    /// <c>CanConnect</c> θα τις έδειχνε ίδιες.
    /// </remarks>
    public async Task<bool> CanConnectToServerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new MySqlConnection(_serverOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    public async Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_serverOnlyConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name;";
        command.Parameters.AddWithValue("@name", DatabaseName);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
    }

    /// <summary>
    /// Δημιουργεί τη βάση με ρητό charset και collation.
    /// </summary>
    /// <remarks>
    /// Γίνεται εδώ και δεν αφήνεται στο EF: η βάση θα κληρονομούσε τις προεπιλογές του
    /// διακομιστή, που μπορεί να είναι <c>latin1</c>. Τα ονόματα sprites και projects
    /// είναι ελεύθερο κείμενο χρήστη — χωρίς utf8mb4 τα ελληνικά και τα emoji
    /// θα αλλοιώνονταν σιωπηλά.
    /// </remarks>
    public async Task CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_serverOnlyConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        // Το όνομα βάσης δεν μπορεί να είναι παράμετρος σε DDL· γι' αυτό
        // επικυρώνεται αυστηρά στον constructor αντί να παρεμβληθεί ως έχει.
        command.CommandText =
            "CREATE DATABASE IF NOT EXISTS `" + DatabaseName + "` " +
            "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MigrationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var applied = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

        var all = _context.Database.GetMigrations().ToList();

        return new MigrationStatus(applied, pending, all);
    }

    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        return _context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Παράγει SQL αντί να το εκτελέσει — για περιβάλλοντα όπου τα σχήματα περνούν
    /// από έλεγχο ή τα εφαρμόζει ο διαχειριστής της βάσης.
    /// </summary>
    /// <param name="idempotent">
    /// Προσθέτει ελέγχους ώστε το script να μπορεί να τρέξει πάνω σε βάση οποιασδήποτε
    /// κατάστασης χωρίς να διπλοεφαρμόσει βήματα.
    /// </param>
    public string GenerateScript(string? fromMigration, string? toMigration, bool idempotent)
    {
        var migrator = _context.GetService<IMigrator>();

        return migrator.GenerateScript(
            fromMigration,
            toMigration,
            idempotent ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync().ConfigureAwait(false);
    }
}
