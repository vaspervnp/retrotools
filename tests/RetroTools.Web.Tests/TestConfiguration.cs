using Microsoft.Extensions.Configuration;

namespace RetroTools.Web.Tests;

/// <summary>
/// Ελέγχει αν υπάρχει ρυθμισμένη βάση, ώστε τα tests που τη χρειάζονται να γίνονται
/// skip αντί για fail σε περιβάλλον χωρίς secrets.
/// </summary>
public static class TestConfiguration
{
    private static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestConfiguration).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    public static bool HasDatabase
    {
        get { return !string.IsNullOrWhiteSpace(Configuration.GetConnectionString("RetroTools")); }
    }
}

/// <summary>Test που απαιτεί ζωντανή MariaDB.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!TestConfiguration.HasDatabase)
        {
            Skip = "Δεν έχει ρυθμιστεί το connection string 'RetroTools'.";
        }
    }
}
