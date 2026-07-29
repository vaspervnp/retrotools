namespace RetroTools.Secrets;

/// <summary>Μια ρύθμιση που περιμένει η εφαρμογή.</summary>
public sealed record KnownSecret(string Key, string Description, bool Required, string? Group = null);

/// <summary>
/// Ο κατάλογος των ρυθμίσεων που διαβάζει η εφαρμογή.
/// </summary>
/// <remarks>
/// Υπάρχει ώστε το εργαλείο να μπορεί να πει «σου λείπει αυτό» αντί να είναι ένας
/// τυφλός επεξεργαστής JSON. Σε διακομιστή, όπου δεν υπάρχει IDE να σε βοηθήσει,
/// η διαφορά είναι μεταξύ «δουλεύει» και «γιατί δεν ξεκινά».
/// </remarks>
public static class KnownSecrets
{
    /// <summary>Πρέπει να ταιριάζει με το UserSecretsId του RetroTools.Web.csproj.</summary>
    public const string DefaultUserSecretsId = "retrotools-spritestudio-2b7f4c19";

    public const string ConnectionStringKey = "ConnectionStrings:RetroTools";

    public static readonly IReadOnlyList<KnownSecret> All = new[]
    {
        new KnownSecret(
            ConnectionStringKey,
            "Σύνδεση MariaDB. Μορφή: Server=...;Port=3306;Database=...;User ID=...;Password=...;",
            Required: true),

        new KnownSecret(
            "Authentication:GitHub:ClientId",
            "GitHub OAuth App → Client ID",
            Required: false,
            Group: "GitHub"),

        new KnownSecret(
            "Authentication:GitHub:ClientSecret",
            "GitHub OAuth App → Client Secret",
            Required: false,
            Group: "GitHub"),

        new KnownSecret(
            "Authentication:Google:ClientId",
            "Google Cloud OAuth 2.0 Client → Client ID",
            Required: false,
            Group: "Google"),

        new KnownSecret(
            "Authentication:Google:ClientSecret",
            "Google Cloud OAuth 2.0 Client → Client Secret",
            Required: false,
            Group: "Google"),
    };

    /// <summary>
    /// Ελέγχει τι λείπει. Οι OAuth providers ελέγχονται <b>ανά ζεύγος</b>: ένα ClientId
    /// χωρίς ClientSecret δεν είναι «μισή ρύθμιση», είναι σφάλμα που θα εμφανιζόταν
    /// σιωπηλά ως «ο provider δεν εμφανίζεται».
    /// </summary>
    public static IReadOnlyList<string> Validate(SecretStore store)
    {
        var problems = new List<string>();

        foreach (var secret in All.Where(s => s.Required))
        {
            if (string.IsNullOrWhiteSpace(store.Get(secret.Key)))
            {
                problems.Add("Λείπει η υποχρεωτική ρύθμιση: " + secret.Key);
            }
        }

        foreach (var group in All.Where(s => s.Group != null).GroupBy(s => s.Group))
        {
            var present = group.Count(s => !string.IsNullOrWhiteSpace(store.Get(s.Key)));

            if (present > 0 && present < group.Count())
            {
                var missing = group
                    .Where(s => string.IsNullOrWhiteSpace(store.Get(s.Key)))
                    .Select(s => s.Key);

                problems.Add(
                    "Ο provider " + group.Key + " είναι μισο-ρυθμισμένος — λείπει: " +
                    string.Join(", ", missing) + ". Ο provider δεν θα ενεργοποιηθεί.");
            }
        }

        return problems;
    }

    /// <summary>
    /// Το όνομα της αντίστοιχης μεταβλητής περιβάλλοντος. Το .NET αντιστοιχίζει την
    /// άνω-κάτω τελεία σε διπλή κάτω παύλα, γιατί η άνω-κάτω τελεία δεν επιτρέπεται
    /// σε ονόματα μεταβλητών σε ορισμένα συστήματα.
    /// </summary>
    public static string ToEnvironmentVariable(string key)
    {
        return key.Replace(":", "__", StringComparison.Ordinal);
    }
}
