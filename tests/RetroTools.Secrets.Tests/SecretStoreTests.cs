using System.Runtime.InteropServices;
using System.Text.Json;

namespace RetroTools.Configuration.Tests;

public class SecretStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public SecretStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retrotools-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "secrets.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Ο καθαρισμός δεν πρέπει να ρίξει το test.
        }
    }

    // --- Διαδρομή αποθηκευτικού χώρου ---------------------------------------

    /// <summary>
    /// Η διαδρομή πρέπει να συμπίπτει με αυτήν που χρησιμοποιεί το SDK, αλλιώς το
    /// εργαλείο θα έγραφε σε αρχείο που δεν διαβάζει ποτέ η εφαρμογή.
    /// </summary>
    [Fact]
    public void Resolved_path_matches_the_dotnet_convention()
    {
        var path = SecretStore.ResolvePath("my-id");

        Assert.EndsWith(Path.Combine("my-id", "secrets.json"), path, StringComparison.Ordinal);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Contains(Path.Combine("Microsoft", "UserSecrets"), path, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(Path.Combine(".microsoft", "usersecrets"), path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Empty_user_secrets_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SecretStore.ResolvePath("  "));
    }

    // --- Ανάγνωση & εγγραφή --------------------------------------------------

    [Fact]
    public void Missing_file_opens_as_an_empty_store()
    {
        var store = SecretStore.OpenFile(_path);

        Assert.False(store.Exists);
        Assert.Equal(0, store.Count);
        Assert.Null(store.Get("οτιδήποτε"));
    }

    [Fact]
    public void Values_round_trip_through_the_file()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set("ConnectionStrings:RetroTools", "Server=x;Password=κωδικός;");
        store.Set("Authentication:GitHub:ClientId", "abc");
        store.Save();

        var reloaded = SecretStore.OpenFile(_path);

        Assert.True(reloaded.Exists);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Server=x;Password=κωδικός;", reloaded.Get("ConnectionStrings:RetroTools"));
        Assert.Equal("abc", reloaded.Get("Authentication:GitHub:ClientId"));
    }

    /// <summary>
    /// Το αρχείο πρέπει να είναι σε <b>επίπεδη</b> μορφή, όπως το γράφει το SDK.
    /// Αν το γράφαμε φωλιασμένο, το `dotnet user-secrets list` δεν θα το διάβαζε.
    /// </summary>
    [Fact]
    public void Saved_file_uses_flat_colon_separated_keys()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set("Authentication:GitHub:ClientId", "abc");
        store.Save();

        using var document = JsonDocument.Parse(File.ReadAllText(_path));

        Assert.True(document.RootElement.TryGetProperty("Authentication:GitHub:ClientId", out var value));
        Assert.Equal("abc", value.GetString());
    }

    /// <summary>
    /// Ένα <c>appsettings.Local.json</c> είναι φωλιασμένο. Το εργαλείο πρέπει να
    /// μπορεί να το διαβάσει για να υποστηρίζει την εντολή <c>import</c>.
    /// </summary>
    [Fact]
    public void Nested_json_is_flattened_on_read()
    {
        File.WriteAllText(
            _path,
            "{ \"ConnectionStrings\": { \"RetroTools\": \"Server=x;\" }," +
            "  \"Authentication\": { \"GitHub\": { \"ClientId\": \"abc\", \"ClientSecret\": \"def\" } } }");

        var store = SecretStore.OpenFile(_path);

        Assert.Equal("Server=x;", store.Get("ConnectionStrings:RetroTools"));
        Assert.Equal("abc", store.Get("Authentication:GitHub:ClientId"));
        Assert.Equal("def", store.Get("Authentication:GitHub:ClientSecret"));
    }

    [Fact]
    public void Comment_pseudo_keys_are_ignored()
    {
        File.WriteAllText(
            _path,
            "{ \"//\": \"αυτό είναι σχόλιο, όχι ρύθμιση\", \"//2\": \"ούτε αυτό\"," +
            "  \"ConnectionStrings\": { \"RetroTools\": \"Server=x;\" } }");

        var store = SecretStore.OpenFile(_path);

        Assert.Equal(1, store.Count);
        Assert.Equal("Server=x;", store.Get("ConnectionStrings:RetroTools"));
    }

    [Fact]
    public void Non_string_values_are_preserved_as_text()
    {
        File.WriteAllText(
            _path,
            "{ \"RetroTools\": { \"BehindReverseProxy\": true, \"Port\": 8080 } }");

        var store = SecretStore.OpenFile(_path);

        Assert.Equal("True", store.Get("RetroTools:BehindReverseProxy"));
        Assert.Equal("8080", store.Get("RetroTools:Port"));
    }

    [Fact]
    public void Greek_values_survive_without_escaping()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set("Δοκιμή", "τιμή με ελληνικά");
        store.Save();

        var text = File.ReadAllText(_path);

        Assert.Contains("τιμή με ελληνικά", text, StringComparison.Ordinal);
        Assert.Equal("τιμή με ελληνικά", SecretStore.OpenFile(_path).Get("Δοκιμή"));
    }

    [Fact]
    public void Remove_and_clear_persist()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set("a", "1");
        store.Set("b", "2");
        store.Save();

        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("δεν-υπάρχει"));
        store.Save();

        Assert.Equal(1, SecretStore.OpenFile(_path).Count);

        store.Clear();
        store.Save();

        Assert.Equal(0, SecretStore.OpenFile(_path).Count);
    }

    /// <summary>
    /// Σε Unix το αρχείο πρέπει να είναι αναγνώσιμο μόνο από τον ιδιοκτήτη.
    /// Σε διακομιστή με πολλές υπηρεσίες, ένα 0644 secrets.json είναι διαρροή.
    /// </summary>
    [Fact]
    public void Saved_file_is_owner_only_on_unix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var store = SecretStore.OpenFile(_path);
        store.Set("a", "1");
        store.Save();

        var mode = File.GetUnixFileMode(_path);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    // --- Απόκρυψη ------------------------------------------------------------

    [Fact]
    public void Mask_hides_the_bulk_of_the_value()
    {
        var masked = SecretStore.Mask("Server=host;Password=κωδικός;");

        Assert.StartsWith("Serv", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("κωδικός", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("host", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "(κενό)")]
    [InlineData("ab", "••")]
    [InlineData("abcd", "••••")]
    public void Mask_reveals_nothing_for_short_values(string value, string expected)
    {
        Assert.Equal(expected, SecretStore.Mask(value));
    }

    // --- Επικύρωση -----------------------------------------------------------

    [Fact]
    public void Validation_reports_the_missing_connection_string()
    {
        var problems = KnownSecrets.Validate(SecretStore.OpenFile(_path));

        Assert.Contains(problems, p => p.Contains(KnownSecrets.ConnectionStringKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// Ένα ClientId χωρίς ClientSecret δεν είναι «μισή ρύθμιση» — ο provider απλώς
    /// δεν εμφανίζεται, και ο διαχειριστής ψάχνει γιατί. Το εργαλείο το λέει.
    /// </summary>
    [Fact]
    public void Validation_catches_a_half_configured_provider()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set(KnownSecrets.ConnectionStringKey, "Server=x;");
        store.Set("Authentication:GitHub:ClientId", "abc");

        var problems = KnownSecrets.Validate(store);

        Assert.Contains(problems, p => p.Contains("GitHub", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("ClientSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_completely_unconfigured_provider_is_not_a_problem()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set(KnownSecrets.ConnectionStringKey, "Server=x;");

        Assert.Empty(KnownSecrets.Validate(store));
    }

    [Fact]
    public void A_fully_configured_provider_is_not_a_problem()
    {
        var store = SecretStore.OpenFile(_path);
        store.Set(KnownSecrets.ConnectionStringKey, "Server=x;");
        store.Set("Authentication:Google:ClientId", "abc");
        store.Set("Authentication:Google:ClientSecret", "def");

        Assert.Empty(KnownSecrets.Validate(store));
    }

    // --- Μεταβλητές περιβάλλοντος -------------------------------------------

    /// <summary>
    /// Το .NET αντιστοιχίζει την άνω-κάτω τελεία σε διπλή κάτω παύλα. Λάθος εδώ
    /// σημαίνει ρυθμίσεις που η εφαρμογή δεν βλέπει ποτέ.
    /// </summary>
    [Theory]
    [InlineData("ConnectionStrings:RetroTools", "ConnectionStrings__RetroTools")]
    [InlineData("Authentication:GitHub:ClientSecret", "Authentication__GitHub__ClientSecret")]
    [InlineData("Simple", "Simple")]
    public void Environment_variable_names_use_double_underscores(string key, string expected)
    {
        Assert.Equal(expected, KnownSecrets.ToEnvironmentVariable(key));
    }

    [Fact]
    public void Known_secrets_id_matches_the_web_project()
    {
        // Αν αποκλίνουν, το εργαλείο θα γράφει σε λάθος φάκελο και η εφαρμογή
        // θα παραπονιέται ότι λείπει το connection string.
        Assert.Equal("retrotools-spritestudio-2b7f4c19", KnownSecrets.DefaultUserSecretsId);
    }
}
