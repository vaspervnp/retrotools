using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroTools.Configuration;

/// <summary>
/// Διαβάζει και γράφει τον ίδιο αποθηκευτικό χώρο με το <c>dotnet user-secrets</c>,
/// <b>χωρίς να χρειάζεται το SDK</b>.
/// </summary>
/// <remarks>
/// <para>
/// Ο «μυστικός» αποθηκευτικός χώρος του .NET δεν είναι κάτι εξωτικό: είναι ένα απλό
/// JSON αρχείο σε γνωστή διαδρομή, με επίπεδα κλειδιά χωρισμένα με άνω-κάτω τελεία
/// (<c>"ConnectionStrings:RetroTools"</c>). Το SDK απλώς το επεξεργάζεται για εσένα.
/// Αυτή η κλάση κάνει το ίδιο, οπότε τα αρχεία παραμένουν πλήρως συμβατά και με τα
/// δύο εργαλεία.
/// </para>
/// <para>
/// <b>Δεν κρυπτογραφείται τίποτα</b> — ούτε το SDK κρυπτογραφεί. Η προστασία είναι
/// ότι το αρχείο ζει έξω από τον φάκελο του project (άρα δεν μπαίνει σε git) και ότι
/// τα δικαιώματά του περιορίζονται στον ιδιοκτήτη.
/// </para>
/// </remarks>
public sealed class SecretStore
{
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SortedDictionary<string, string> _values =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    private SecretStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public bool Exists
    {
        get { return File.Exists(Path); }
    }

    public int Count
    {
        get { return _values.Count; }
    }

    public IReadOnlyDictionary<string, string> Values
    {
        get { return _values; }
    }

    /// <summary>
    /// Η διαδρομή που χρησιμοποιεί το .NET για ένα δεδομένο <c>UserSecretsId</c>.
    /// Διαφέρει ανά λειτουργικό, γι' αυτό δεν γράφεται ποτέ με το χέρι.
    /// </summary>
    public static string ResolvePath(string userSecretsId)
    {
        if (string.IsNullOrWhiteSpace(userSecretsId))
        {
            throw new ArgumentException("Το UserSecretsId δεν μπορεί να είναι κενό.", nameof(userSecretsId));
        }

        var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets")
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets");

        return System.IO.Path.Combine(root, userSecretsId, "secrets.json");
    }

    public static SecretStore OpenUserSecrets(string userSecretsId)
    {
        return OpenFile(ResolvePath(userSecretsId));
    }

    /// <summary>Ανοίγει αυθαίρετο αρχείο, π.χ. <c>appsettings.Local.json</c>.</summary>
    public static SecretStore OpenFile(string path)
    {
        var store = new SecretStore(path);

        if (!File.Exists(path))
        {
            return store;
        }

        var json = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(json))
        {
            return store;
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        // Δεχόμαστε και τις δύο μορφές: επίπεδα κλειδιά με άνω-κάτω τελεία (όπως
        // γράφει το SDK) και φωλιασμένα αντικείμενα (όπως γράφει ένα appsettings).
        Flatten(document.RootElement, string.Empty, store._values);

        return store;
    }

    private static void Flatten(JsonElement element, string prefix, IDictionary<string, string> target)
    {
        foreach (var property in element.EnumerateObject())
        {
            // Τα σχόλια-ψευδοκλειδιά των template αρχείων μας δεν είναι ρυθμίσεις.
            if (property.Name.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var key = prefix.Length == 0 ? property.Name : prefix + ":" + property.Name;

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, key, target);
                    break;

                case JsonValueKind.String:
                    target[key] = property.Value.GetString() ?? string.Empty;
                    break;

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    target[key] = property.Value.ToString();
                    break;

                case JsonValueKind.Null:
                    target[key] = string.Empty;
                    break;
            }
        }
    }

    public string? Get(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Το κλειδί δεν μπορεί να είναι κενό.", nameof(key));
        }

        _values[key] = value;
    }

    public bool Remove(string key)
    {
        return _values.Remove(key);
    }

    public void Clear()
    {
        _values.Clear();
    }

    /// <summary>
    /// Γράφει το αρχείο σε επίπεδη μορφή και περιορίζει τα δικαιώματά του.
    /// </summary>
    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(_values, WriteOptions));

        RestrictPermissions(Path);
    }

    /// <summary>
    /// Σε Unix το αρχείο γίνεται αναγνώσιμο <b>μόνο από τον ιδιοκτήτη</b> (0600).
    /// Χωρίς αυτό, ένα secrets.json με προεπιλεγμένα δικαιώματα είναι αναγνώσιμο από
    /// κάθε λογαριασμό του μηχανήματος — που σε διακομιστή με πολλές υπηρεσίες μετράει.
    /// Σε Windows τα NTFS ACL του φακέλου χρήστη καλύπτουν ήδη την περίπτωση.
    /// </summary>
    private static void RestrictPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Σε ασυνήθιστα συστήματα αρχείων μπορεί να μην υποστηρίζεται· η αποθήκευση
            // δεν πρέπει να αποτύχει γι' αυτό, αλλά ο χρήστης ενημερώνεται από το Program.
        }
    }

    /// <summary>
    /// Αποκρύπτει την τιμή για εμφάνιση. Κρατά λίγους χαρακτήρες στην αρχή ώστε να
    /// μπορείς να ξεχωρίσεις ποια τιμή είναι, χωρίς να τη διαβάσει κάποιος πίσω σου.
    /// </summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(κενό)";
        }

        if (value.Length <= 4)
        {
            return new string('•', value.Length);
        }

        var visible = Math.Min(4, value.Length / 4);

        return value.Substring(0, visible) + new string('•', Math.Min(20, value.Length - visible));
    }
}
