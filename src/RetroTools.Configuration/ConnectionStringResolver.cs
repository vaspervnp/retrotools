namespace RetroTools.Configuration;

/// <summary>Από πού βρέθηκε τελικά το connection string.</summary>
public enum ConnectionStringSource
{
    None = 0,
    CommandLine = 1,
    EnvironmentVariable = 2,
    File = 3,
    UserSecrets = 4,
}

public sealed record ResolvedConnectionString(string? Value, ConnectionStringSource Source, string? Location)
{
    public bool Found
    {
        get { return !string.IsNullOrWhiteSpace(Value); }
    }

    /// <summary>Περιγραφή της πηγής για εμφάνιση — <b>χωρίς</b> την τιμή.</summary>
    public string Describe()
    {
        return Source switch
        {
            ConnectionStringSource.CommandLine => "παράμετρος --connection",
            ConnectionStringSource.EnvironmentVariable => "μεταβλητή περιβάλλοντος " + Location,
            ConnectionStringSource.File => "αρχείο " + Location,
            ConnectionStringSource.UserSecrets => "user-secrets: " + Location,
            _ => "δεν βρέθηκε",
        };
    }
}

/// <summary>
/// Εντοπίζει το connection string με την <b>ίδια σειρά προτεραιότητας</b> για όλα τα
/// εργαλεία.
/// </summary>
/// <remarks>
/// Υπάρχει επειδή δύο εργαλεία με διαφορετική ιδέα για το πού ζουν οι ρυθμίσεις είναι
/// χειρότερα από κανένα εργαλείο: θα ρύθμιζες με το ένα και το άλλο θα έλεγε «λείπει».
/// Η σειρά ταιριάζει με αυτήν της εφαρμογής — το ρητό υπερισχύει του σιωπηρού.
/// </remarks>
public static class ConnectionStringResolver
{
    public static readonly string EnvironmentVariableName =
        KnownSecrets.ToEnvironmentVariable(KnownSecrets.ConnectionStringKey);

    public static ResolvedConnectionString Resolve(
        string? explicitValue = null,
        string? file = null,
        string? userSecretsId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return new ResolvedConnectionString(explicitValue, ConnectionStringSource.CommandLine, null);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new ResolvedConnectionString(
                fromEnvironment,
                ConnectionStringSource.EnvironmentVariable,
                EnvironmentVariableName);
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            var store = SecretStore.OpenFile(file);
            var value = store.Get(KnownSecrets.ConnectionStringKey);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return new ResolvedConnectionString(value, ConnectionStringSource.File, file);
            }
        }

        var id = string.IsNullOrWhiteSpace(userSecretsId) ? KnownSecrets.DefaultUserSecretsId : userSecretsId;
        var secrets = SecretStore.OpenUserSecrets(id);
        var fromSecrets = secrets.Get(KnownSecrets.ConnectionStringKey);

        if (!string.IsNullOrWhiteSpace(fromSecrets))
        {
            return new ResolvedConnectionString(fromSecrets, ConnectionStringSource.UserSecrets, secrets.Path);
        }

        return new ResolvedConnectionString(null, ConnectionStringSource.None, null);
    }

    /// <summary>
    /// Μήνυμα που λέει στον χρήστη <b>τι να κάνει</b>, όχι μόνο τι απέτυχε.
    /// </summary>
    public static string BuildMissingMessage()
    {
        return "Δεν βρέθηκε connection string. Δώσ' το με έναν από τους παρακάτω τρόπους:"
               + Environment.NewLine + "  --connection \"Server=...;Database=...;User ID=...;Password=...;\""
               + Environment.NewLine + "  " + EnvironmentVariableName + "=\"...\""
               + Environment.NewLine + "  retrotools-secrets set \"" + KnownSecrets.ConnectionStringKey + "\""
               + Environment.NewLine + "  --file appsettings.Local.json";
    }
}
