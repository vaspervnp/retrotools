using MySqlConnector;
using RetroTools.Configuration;

// Εργαλείο ρύθμισης secrets που δεν χρειάζεται .NET SDK.
//
// Το `dotnet user-secrets` είναι εντολή του SDK. Σε διακομιστή παραγωγής το SDK
// συνήθως δεν υπάρχει — και ενδεχομένως ούτε καν το runtime, αν η εφαρμογή τρέχει
// ως self-contained. Αυτό το εργαλείο χειρίζεται το ίδιο αρχείο απευθείας και
// μπορεί να δημοσιευτεί self-contained ώστε να τρέχει σε γυμνό μηχάνημα.

const int ExitOk = 0;
const int ExitError = 1;
const int ExitInvalid = 2;

var arguments = new List<string>(args);
var reveal = TakeFlag(arguments, "--reveal");
var force = TakeFlag(arguments, "--force");
var userSecretsId = TakeOption(arguments, "--id") ?? KnownSecrets.DefaultUserSecretsId;
var filePath = TakeOption(arguments, "--file");

if (arguments.Count == 0 || IsHelp(arguments[0]))
{
    PrintUsage();
    return ExitOk;
}

var command = arguments[0].ToLowerInvariant();
var operands = arguments.Skip(1).ToList();

SecretStore store;

try
{
    store = filePath != null
        ? SecretStore.OpenFile(filePath)
        : SecretStore.OpenUserSecrets(userSecretsId);
}
catch (Exception exception)
{
    Console.Error.WriteLine("Δεν άνοιξε ο αποθηκευτικός χώρος: " + exception.Message);
    return ExitError;
}

try
{
    return command switch
    {
        "path" => CommandPath(store),
        "list" => CommandList(store, reveal),
        "get" => CommandGet(store, operands, reveal),
        "set" => CommandSet(store, operands),
        "remove" => CommandRemove(store, operands),
        "clear" => CommandClear(store, force),
        "export-env" => CommandExportEnv(store),
        "import" => CommandImport(store, operands),
        "check" => CommandCheck(store),
        "test" => await CommandTestAsync(store),
        _ => Unknown(command),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine("Σφάλμα: " + exception.Message);
    return ExitError;
}

// --- Εντολές ----------------------------------------------------------------

int CommandPath(SecretStore target)
{
    Console.WriteLine(target.Path);
    Console.WriteLine(target.Exists ? "(υπάρχει)" : "(δεν υπάρχει ακόμη — θα δημιουργηθεί στο πρώτο set)");

    return ExitOk;
}

int CommandList(SecretStore target, bool revealValues)
{
    Console.WriteLine("Αποθηκευτικός χώρος: " + target.Path);
    Console.WriteLine();

    foreach (var known in KnownSecrets.All)
    {
        var value = target.Get(known.Key);
        var status = string.IsNullOrWhiteSpace(value)
            ? (known.Required ? "ΛΕΙΠΕΙ  " : "—       ")
            : "ok      ";

        var shown = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : (revealValues ? value : SecretStore.Mask(value));

        Console.WriteLine(status + known.Key);

        if (shown.Length > 0)
        {
            Console.WriteLine("          " + shown);
        }
    }

    var extra = target.Values.Keys.Except(KnownSecrets.All.Select(s => s.Key), StringComparer.Ordinal).ToList();

    if (extra.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Επιπλέον κλειδιά που δεν αναγνωρίζει η εφαρμογή:");

        foreach (var key in extra)
        {
            Console.WriteLine("  " + key + " = " +
                              (revealValues ? target.Get(key) : SecretStore.Mask(target.Get(key))));
        }
    }

    if (!revealValues)
    {
        Console.WriteLine();
        Console.WriteLine("(Οι τιμές είναι κρυμμένες. Με --reveal εμφανίζονται ολόκληρες.)");
    }

    return ExitOk;
}

int CommandGet(SecretStore target, IReadOnlyList<string> operandList, bool revealValue)
{
    if (operandList.Count < 1)
    {
        Console.Error.WriteLine("Χρήση: retrotools-secrets get <κλειδί> [--reveal]");
        return ExitError;
    }

    var value = target.Get(operandList[0]);

    if (value == null)
    {
        Console.Error.WriteLine("Το κλειδί δεν υπάρχει: " + operandList[0]);
        return ExitInvalid;
    }

    // Χωρίς --reveal η τιμή δεν εκτυπώνεται ολόκληρη: σε διακομιστή η κονσόλα
    // καταλήγει συχνά σε logs ή σε session recording.
    Console.WriteLine(revealValue ? value : SecretStore.Mask(value));

    return ExitOk;
}

int CommandSet(SecretStore target, IReadOnlyList<string> operandList)
{
    if (operandList.Count < 1)
    {
        Console.Error.WriteLine("Χρήση: retrotools-secrets set <κλειδί> [τιμή]");
        Console.Error.WriteLine("Χωρίς τιμή, διαβάζεται από το stdin — έτσι δεν μένει στο ιστορικό του shell.");
        return ExitError;
    }

    var key = operandList[0];
    string value;

    if (operandList.Count >= 2)
    {
        value = string.Join(' ', operandList.Skip(1));
    }
    else
    {
        Console.Error.WriteLine("Τιμή για '" + key + "' (τέλος με Enter):");
        value = Console.ReadLine() ?? string.Empty;
    }

    if (string.IsNullOrEmpty(value))
    {
        Console.Error.WriteLine("Η τιμή είναι κενή — χρησιμοποίησε 'remove' για διαγραφή.");
        return ExitError;
    }

    target.Set(key, value);
    target.Save();

    Console.WriteLine("Αποθηκεύτηκε: " + key + " = " + SecretStore.Mask(value));
    Console.WriteLine("Αρχείο: " + target.Path);

    return ExitOk;
}

int CommandRemove(SecretStore target, IReadOnlyList<string> operandList)
{
    if (operandList.Count < 1)
    {
        Console.Error.WriteLine("Χρήση: retrotools-secrets remove <κλειδί>");
        return ExitError;
    }

    if (!target.Remove(operandList[0]))
    {
        Console.Error.WriteLine("Το κλειδί δεν υπήρχε: " + operandList[0]);
        return ExitInvalid;
    }

    target.Save();
    Console.WriteLine("Διαγράφηκε: " + operandList[0]);

    return ExitOk;
}

int CommandClear(SecretStore target, bool confirmed)
{
    if (!confirmed)
    {
        Console.Error.WriteLine(
            "Αυτό διαγράφει " + target.Count + " ρυθμίσεις από " + target.Path + ".");
        Console.Error.WriteLine("Αν είσαι σίγουρος, ξανατρέξε με --force.");
        return ExitError;
    }

    target.Clear();
    target.Save();

    Console.WriteLine("Ο αποθηκευτικός χώρος άδειασε.");

    return ExitOk;
}

/// <summary>
/// Παράγει γραμμές για systemd EnvironmentFile ή για <c>source</c> σε shell.
/// Χρήσιμο όταν προτιμάς μεταβλητές περιβάλλοντος αντί για αρχείο secrets.
/// </summary>
int CommandExportEnv(SecretStore target)
{
    if (target.Count == 0)
    {
        Console.Error.WriteLine("Δεν υπάρχουν ρυθμίσεις προς εξαγωγή.");
        return ExitInvalid;
    }

    foreach (var pair in target.Values)
    {
        // Μονά εισαγωγικά ώστε το shell να μην ερμηνεύσει $ ή backtick μέσα σε κωδικό.
        var escaped = pair.Value.Replace("'", "'\\''", StringComparison.Ordinal);
        Console.WriteLine(KnownSecrets.ToEnvironmentVariable(pair.Key) + "='" + escaped + "'");
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine("Προσοχή: η έξοδος περιέχει τις τιμές σε καθαρό κείμενο.");
    Console.Error.WriteLine("Ανακατεύθυνέ την σε αρχείο με περιορισμένα δικαιώματα (chmod 600).");

    return ExitOk;
}

int CommandImport(SecretStore target, IReadOnlyList<string> operandList)
{
    if (operandList.Count < 1)
    {
        Console.Error.WriteLine("Χρήση: retrotools-secrets import <αρχείο.json>");
        return ExitError;
    }

    if (!File.Exists(operandList[0]))
    {
        Console.Error.WriteLine("Δεν βρέθηκε το αρχείο: " + operandList[0]);
        return ExitError;
    }

    var source = SecretStore.OpenFile(operandList[0]);
    var imported = 0;

    foreach (var pair in source.Values)
    {
        // Οι placeholder τιμές των template αρχείων δεν πρέπει να καταλήξουν ρυθμίσεις.
        if (LooksLikePlaceholder(pair.Value))
        {
            Console.Error.WriteLine("Παραλείπεται placeholder: " + pair.Key + " = " + pair.Value);
            continue;
        }

        target.Set(pair.Key, pair.Value);
        imported++;
    }

    target.Save();

    Console.WriteLine("Εισήχθησαν " + imported + " ρυθμίσεις στο " + target.Path);

    return ExitOk;
}

int CommandCheck(SecretStore target)
{
    var problems = KnownSecrets.Validate(target);

    if (problems.Count == 0)
    {
        Console.WriteLine("Όλα εντάξει: οι υποχρεωτικές ρυθμίσεις υπάρχουν και οι providers είναι πλήρεις.");
        return ExitOk;
    }

    foreach (var problem in problems)
    {
        Console.Error.WriteLine("• " + problem);
    }

    return ExitInvalid;
}

/// <summary>
/// Ο ουσιαστικός έλεγχος: ανοίγει πραγματική σύνδεση στη βάση. Το να υπάρχει
/// connection string δεν σημαίνει ότι δουλεύει — λάθος κωδικός, κλειστό firewall
/// ή λάθος όνομα βάσης φαίνονται μόνο έτσι.
/// </summary>
async Task<int> CommandTestAsync(SecretStore target)
{
    var status = CommandCheck(target);
    Console.WriteLine();

    var connectionString = target.Get(KnownSecrets.ConnectionStringKey);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Χωρίς connection string δεν γίνεται δοκιμή σύνδεσης.");
        return ExitInvalid;
    }

    string host;

    try
    {
        host = new MySqlConnectionStringBuilder(connectionString).Server;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("Το connection string δεν αναλύεται: " + exception.Message);
        return ExitInvalid;
    }

    Console.WriteLine("Δοκιμή σύνδεσης στον " + host + "…");

    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE(), @@version_comment;";

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Console.WriteLine("  Σύνδεση: OK");
        Console.WriteLine("  Έκδοση:  " + connection.ServerVersion);
        Console.WriteLine("  Βάση:    " + (reader.IsDBNull(0) ? "(καμία επιλεγμένη)" : reader.GetString(0)));

        if (!connection.ServerVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("  Προσοχή: ο διακομιστής δεν δηλώνει MariaDB.");
        }
    }
    catch (MySqlException exception)
    {
        // Το μήνυμα του driver λέει ήδη τι φταίει (access denied, unknown database,
        // timeout) — δεν το ξαναγράφουμε με δικά μας λόγια.
        Console.Error.WriteLine("  Σύνδεση: ΑΠΕΤΥΧΕ — " + exception.Message);
        return ExitInvalid;
    }

    return status;
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

static bool LooksLikePlaceholder(string value)
{
    return value.Contains("YOUR_", StringComparison.Ordinal)
           || value.Contains("DB_HOST", StringComparison.Ordinal)
           || value.Contains("DB_USER", StringComparison.Ordinal)
           || value.Contains("DB_PASSWORD", StringComparison.Ordinal)
           || value.Contains("DB_NAME", StringComparison.Ordinal)
           || value.Contains("OAUTH_CLIENT", StringComparison.Ordinal);
}

static void PrintUsage()
{
    Console.WriteLine("retrotools-secrets — ρύθμιση secrets χωρίς .NET SDK");
    Console.WriteLine();
    Console.WriteLine("ΕΝΤΟΛΕΣ");
    Console.WriteLine("  path                    Πού βρίσκεται το αρχείο ρυθμίσεων");
    Console.WriteLine("  list                    Όλες οι ρυθμίσεις (τιμές κρυμμένες)");
    Console.WriteLine("  get <κλειδί>            Μία τιμή");
    Console.WriteLine("  set <κλειδί> [τιμή]     Ορισμός· χωρίς τιμή διαβάζει από stdin");
    Console.WriteLine("  remove <κλειδί>         Διαγραφή");
    Console.WriteLine("  clear --force           Διαγραφή όλων");
    Console.WriteLine("  import <αρχείο.json>    Εισαγωγή από appsettings.Local.json κ.λπ.");
    Console.WriteLine("  export-env              Γραμμές για systemd EnvironmentFile");
    Console.WriteLine("  check                   Λείπει κάτι;");
    Console.WriteLine("  test                    check + πραγματική σύνδεση στη βάση");
    Console.WriteLine();
    Console.WriteLine("ΕΠΙΛΟΓΕΣ");
    Console.WriteLine("  --file <διαδρομή>       Χρήση αρχείου αντί για τον user-secrets χώρο");
    Console.WriteLine("  --id <UserSecretsId>    Άλλο UserSecretsId (default: " + KnownSecrets.DefaultUserSecretsId + ")");
    Console.WriteLine("  --reveal                Εμφάνιση ολόκληρων των τιμών");
    Console.WriteLine("  --force                 Επιβεβαίωση καταστροφικών ενεργειών");
    Console.WriteLine();
    Console.WriteLine("ΚΩΔΙΚΟΙ ΕΞΟΔΟΥ");
    Console.WriteLine("  0 επιτυχία · 1 σφάλμα χρήσης · 2 λείπει ρύθμιση ή απέτυχε η σύνδεση");
    Console.WriteLine();
    Console.WriteLine("ΠΑΡΑΔΕΙΓΜΑΤΑ");
    Console.WriteLine("  retrotools-secrets set \"ConnectionStrings:RetroTools\"");
    Console.WriteLine("  retrotools-secrets test");
    Console.WriteLine("  retrotools-secrets export-env > /etc/retrotools.env && chmod 600 /etc/retrotools.env");
}
