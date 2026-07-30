using MySqlConnector;
using RetroTools.Configuration;
using RetroTools.Migrator;

// Εργαλείο εφαρμογής migrations χωρίς .NET SDK.
//
// Τα migrations είναι μεταγλωττισμένα μέσα στο RetroTools.Data· το `dotnet ef`
// χρειάζεται μόνο για να τα δημιουργήσει. Για να τα εφαρμόσει αρκεί το EF Core
// runtime, που αυτό το εκτελέσιμο κουβαλά μαζί του.

const int ExitOk = 0;
const int ExitError = 1;
const int ExitPending = 2;

var arguments = new List<string>(args);
var assumeYes = TakeFlag(arguments, "--yes") || TakeFlag(arguments, "-y");
var createDatabase = TakeFlag(arguments, "--create-database");
var idempotent = !TakeFlag(arguments, "--no-idempotent");
var connectionOption = TakeOption(arguments, "--connection");
var fileOption = TakeOption(arguments, "--file");
var idOption = TakeOption(arguments, "--id");
var outputOption = TakeOption(arguments, "--output");
var fromOption = TakeOption(arguments, "--from");
var toOption = TakeOption(arguments, "--to");
var timeout = ParseTimeout(TakeOption(arguments, "--timeout"));

var command = arguments.Count == 0 ? "status" : arguments[0].ToLowerInvariant();

if (IsHelp(command))
{
    PrintUsage();
    return ExitOk;
}

var resolved = ConnectionStringResolver.Resolve(connectionOption, fileOption, idOption);

if (!resolved.Found)
{
    Console.Error.WriteLine(ConnectionStringResolver.BuildMissingMessage());
    return ExitError;
}

MigrationRunner runner;

try
{
    runner = new MigrationRunner(resolved.Value!, timeout);
}
catch (Exception exception)
{
    Console.Error.WriteLine("Το connection string δεν αναλύεται: " + exception.Message);
    return ExitError;
}

await using (runner)
{
    try
    {
        return command switch
        {
            "status" => await CommandStatusAsync(runner, resolved),
            "list" => await CommandListAsync(runner),
            "up" or "apply" => await CommandUpAsync(runner, resolved, assumeYes, createDatabase),
            "script" => await CommandScriptAsync(runner, idempotent, fromOption, toOption, outputOption),
            _ => Unknown(command),
        };
    }
    catch (MySqlException exception)
    {
        Console.Error.WriteLine("Σφάλμα βάσης: " + exception.Message);
        return ExitError;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("Σφάλμα: " + exception.Message);
        return ExitError;
    }
}

// --- Εντολές ----------------------------------------------------------------

async Task<int> CommandStatusAsync(MigrationRunner target, ResolvedConnectionString source)
{
    Console.WriteLine("Διακομιστής: " + target.Host);
    Console.WriteLine("Βάση: " + target.DatabaseName);
    Console.WriteLine("Πηγή ρύθμισης: " + source.Describe());
    Console.WriteLine();

    if (!await target.CanConnectAsync())
    {
        return await ReportConnectionProblemAsync(target);
    }

    var status = await target.GetStatusAsync();

    Console.WriteLine("Εφαρμοσμένα: " + status.Applied.Count);
    Console.WriteLine("Εκκρεμούν:   " + status.Pending.Count);

    if (status.Pending.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Θα εφαρμοστούν με 'up':");

        foreach (var migration in status.Pending)
        {
            Console.WriteLine("  + " + migration);
        }
    }

    if (status.Unknown.Count > 0)
    {
        Console.WriteLine();
        Console.Error.WriteLine("ΠΡΟΣΟΧΗ: η βάση έχει migrations που δεν γνωρίζει αυτό το εκτελέσιμο:");

        foreach (var migration in status.Unknown)
        {
            Console.Error.WriteLine("  ? " + migration);
        }

        Console.Error.WriteLine("Η βάση είναι νεότερη από το εργαλείο — μη συνεχίσεις πριν το ελέγξεις.");
        return ExitError;
    }

    if (status.UpToDate)
    {
        Console.WriteLine();
        Console.WriteLine("Η βάση είναι ενημερωμένη.");
        return ExitOk;
    }

    // Κωδικός 2 και όχι 0: ένα script εγκατάστασης θέλει να ξεχωρίζει το
    // «όλα εντάξει» από το «χρειάζεται ενέργεια» χωρίς να διαβάζει κείμενο.
    return ExitPending;
}

async Task<int> CommandListAsync(MigrationRunner target)
{
    var status = await target.GetStatusAsync();
    var applied = status.Applied.ToHashSet(StringComparer.Ordinal);

    foreach (var migration in status.All)
    {
        Console.WriteLine((applied.Contains(migration) ? "[✓] " : "[ ] ") + migration);
    }

    Console.WriteLine();
    Console.WriteLine(status.All.Count + " migrations συνολικά, " + status.Applied.Count + " εφαρμοσμένα.");

    return ExitOk;
}

async Task<int> CommandUpAsync(
    MigrationRunner target,
    ResolvedConnectionString source,
    bool confirmed,
    bool mayCreateDatabase)
{
    Console.WriteLine("Διακομιστής: " + target.Host);
    Console.WriteLine("Βάση: " + target.DatabaseName);
    Console.WriteLine("Πηγή ρύθμισης: " + source.Describe());
    Console.WriteLine();

    if (!await target.CanConnectAsync())
    {
        // Σε νέο διακομιστή η βάση δεν υπάρχει ακόμη. Η δημιουργία απαιτεί ρητή
        // έγκριση: αν το όνομα έχει τυπογραφικό, δεν πρέπει να φτιαχτεί σιωπηλά
        // μια άδεια βάση με λάθος όνομα και να «δουλέψουν» όλα φαινομενικά.
        if (!await target.CanConnectToServerAsync())
        {
            Console.Error.WriteLine("Δεν γίνεται σύνδεση στον διακομιστή — έλεγξε host, χρήστη και κωδικό.");
            return ExitError;
        }

        if (await target.DatabaseExistsAsync())
        {
            Console.Error.WriteLine(
                "Ο διακομιστής απαντά και η βάση '" + target.DatabaseName +
                "' υπάρχει, αλλά ο χρήστης δεν έχει πρόσβαση σε αυτήν.");
            return ExitError;
        }

        Console.WriteLine("Η βάση '" + target.DatabaseName + "' δεν υπάρχει.");

        if (!mayCreateDatabase)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Για να τη δημιουργήσω, ξανατρέξε με --create-database.");
            Console.Error.WriteLine("Έλεγξε πρώτα ότι το όνομα είναι σωστό — δεν το μαντεύω.");
            return ExitError;
        }

        Console.WriteLine("Δημιουργία με utf8mb4 / utf8mb4_unicode_ci…");
        await target.CreateDatabaseAsync();
        Console.WriteLine("Δημιουργήθηκε.");
        Console.WriteLine();
    }

    var status = await target.GetStatusAsync();

    if (status.Unknown.Count > 0)
    {
        Console.Error.WriteLine(
            "Άρνηση: η βάση έχει " + status.Unknown.Count +
            " migrations που δεν γνωρίζει αυτό το εκτελέσιμο (" +
            string.Join(", ", status.Unknown) + ").");
        Console.Error.WriteLine("Χρησιμοποίησε την έκδοση του εργαλείου που ταιριάζει με τη βάση.");
        return ExitError;
    }

    if (status.UpToDate)
    {
        Console.WriteLine("Δεν εκκρεμεί κανένα migration — καμία αλλαγή.");
        return ExitOk;
    }

    Console.WriteLine("Θα εφαρμοστούν " + status.Pending.Count + " migrations:");

    foreach (var migration in status.Pending)
    {
        Console.WriteLine("  + " + migration);
    }

    Console.WriteLine();

    // Η αλλαγή σχήματος είναι δύσκολα αναστρέψιμη: το EF δεν κάνει rollback αν κάτι
    // αποτύχει στη μέση, και η MariaDB δεν έχει transactional DDL. Ο χρήστης πρέπει
    // να το επιβεβαιώσει ρητά — ή να το δηλώσει με --yes σε script.
    Console.WriteLine("Οι αλλαγές σχήματος στη MariaDB ΔΕΝ είναι transactional:");
    Console.WriteLine("αν κάτι αποτύχει στη μέση, η βάση μένει μισοενημερωμένη.");
    Console.WriteLine("Πάρε αντίγραφο ασφαλείας πρώτα (mysqldump).");
    Console.WriteLine();

    if (!confirmed)
    {
        Console.Write("Να συνεχίσω; (γράψε ναι) ");
        var answer = Console.ReadLine()?.Trim();

        if (!string.Equals(answer, "ναι", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Ακυρώθηκε — καμία αλλαγή.");
            return ExitOk;
        }
    }

    var started = DateTime.UtcNow;
    Console.WriteLine("Εφαρμογή…");

    await target.ApplyAsync();

    var after = await target.GetStatusAsync();
    var elapsed = DateTime.UtcNow - started;

    Console.WriteLine(
        "Ολοκληρώθηκε σε " + elapsed.TotalSeconds.ToString("0.0") + "s. " +
        "Εφαρμοσμένα: " + after.Applied.Count + ", εκκρεμούν: " + after.Pending.Count + ".");

    return after.UpToDate ? ExitOk : ExitPending;
}

async Task<int> CommandScriptAsync(
    MigrationRunner target,
    bool useIdempotent,
    string? from,
    string? to,
    string? output)
{
    // Το script παράγεται από τα μεταγλωττισμένα migrations και δεν αγγίζει τη βάση,
    // οπότε δουλεύει και χωρίς σύνδεση — χρήσιμο για έλεγχο πριν την πρόσβαση.
    var sql = target.GenerateScript(from, to, useIdempotent);

    if (string.IsNullOrWhiteSpace(output))
    {
        Console.Out.Write(sql);
        return ExitOk;
    }

    await File.WriteAllTextAsync(output, sql);

    Console.WriteLine("Γράφτηκε: " + output + " (" + sql.Length.ToString("N0") + " χαρακτήρες)");
    Console.WriteLine(useIdempotent
        ? "Idempotent: μπορεί να τρέξει πάνω σε βάση οποιασδήποτε κατάστασης."
        : "ΠΡΟΣΟΧΗ: όχι idempotent — τρέχει μόνο πάνω στην αναμενόμενη κατάσταση.");

    return ExitOk;
}

/// <summary>
/// Ξεχωρίζει τα τρία σενάρια αποτυχίας σύνδεσης, γιατί έχουν διαφορετική λύση:
/// απρόσιτος διακομιστής, ανύπαρκτη βάση, ή βάση χωρίς δικαιώματα.
/// </summary>
async Task<int> ReportConnectionProblemAsync(MigrationRunner target)
{
    if (!await target.CanConnectToServerAsync())
    {
        Console.Error.WriteLine("Δεν γίνεται σύνδεση στον διακομιστή — έλεγξε host, χρήστη και κωδικό.");
        return ExitError;
    }

    if (await target.DatabaseExistsAsync())
    {
        Console.Error.WriteLine(
            "Η βάση '" + target.DatabaseName + "' υπάρχει αλλά ο χρήστης δεν έχει πρόσβαση σε αυτήν.");
        return ExitError;
    }

    Console.Error.WriteLine("Η βάση '" + target.DatabaseName + "' δεν υπάρχει.");
    Console.Error.WriteLine("Δημιούργησέ την με: retrotools-migrate up --create-database");

    return ExitError;
}

int Unknown(string name)
{
    Console.Error.WriteLine("Άγνωστη εντολή: " + name);
    Console.Error.WriteLine();
    PrintUsage();

    return ExitError;
}

// --- Βοηθητικά ---------------------------------------------------------------

static bool IsHelp(string value)
{
    return value is "-h" or "--help" or "help" or "-?" or "/?";
}

static bool TakeFlag(List<string> list, string name)
{
    var index = list.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    if (index < 0)
    {
        return false;
    }

    list.RemoveAt(index);

    return true;
}

static string? TakeOption(List<string> list, string name)
{
    var index = list.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    if (index < 0 || index + 1 >= list.Count)
    {
        return null;
    }

    var value = list[index + 1];
    list.RemoveRange(index, 2);

    return value;
}

static int ParseTimeout(string? value)
{
    return int.TryParse(value, out var seconds) && seconds > 0 ? seconds : 300;
}

static void PrintUsage()
{
    Console.WriteLine("retrotools-migrate — εφαρμογή migrations χωρίς .NET SDK");
    Console.WriteLine();
    Console.WriteLine("ΕΝΤΟΛΕΣ");
    Console.WriteLine("  status                  Τι εκκρεμεί (προεπιλογή)");
    Console.WriteLine("  list                    Όλα τα migrations, με σημάδι τα εφαρμοσμένα");
    Console.WriteLine("  up                      Εφαρμογή των εκκρεμών (ζητά επιβεβαίωση)");
    Console.WriteLine("  script                  Παραγωγή SQL αντί εκτέλεσης");
    Console.WriteLine();
    Console.WriteLine("ΕΠΙΛΟΓΕΣ");
    Console.WriteLine("  --connection <cs>       Ρητό connection string");
    Console.WriteLine("  --file <διαδρομή>       Ανάγνωση από appsettings.Local.json");
    Console.WriteLine("  --id <UserSecretsId>    Άλλο UserSecretsId");
    Console.WriteLine("  --yes, -y               Χωρίς ερώτηση επιβεβαίωσης (για scripts)");
    Console.WriteLine("  --create-database       Δημιουργία της βάσης αν λείπει (utf8mb4)");
    Console.WriteLine("  --timeout <δευτ.>       Timeout εντολών, προεπιλογή 300");
    Console.WriteLine("  --output <αρχείο>       Αποθήκευση του script σε αρχείο");
    Console.WriteLine("  --from / --to <όνομα>   Εύρος migrations για το script");
    Console.WriteLine("  --no-idempotent         Script χωρίς ελέγχους κατάστασης");
    Console.WriteLine();
    Console.WriteLine("ΣΕΙΡΑ ΑΝΑΖΗΤΗΣΗΣ ΡΥΘΜΙΣΗΣ");
    Console.WriteLine("  --connection → " + ConnectionStringResolver.EnvironmentVariableName +
                      " → --file → user-secrets");
    Console.WriteLine();
    Console.WriteLine("ΚΩΔΙΚΟΙ ΕΞΟΔΟΥ");
    Console.WriteLine("  0 ενημερωμένη ή επιτυχία · 1 σφάλμα · 2 εκκρεμούν migrations");
    Console.WriteLine();
    Console.WriteLine("ΠΑΡΑΔΕΙΓΜΑΤΑ");
    Console.WriteLine("  retrotools-migrate status");
    Console.WriteLine("  retrotools-migrate up --yes");
    Console.WriteLine("  retrotools-migrate script --output schema.sql");
}
