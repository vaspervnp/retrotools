using Microsoft.Extensions.Configuration;

namespace RetroTools.Data.Tests;

/// <summary>
/// Παρέχει ρυθμίσεις στα integration tests χωρίς να υπάρχει ποτέ connection string στο repository.
/// Πηγές, κατά σειρά προτεραιότητας: environment variables → user secrets.
/// </summary>
public static class TestConfiguration
{
    public const string ConnectionStringName = "RetroTools";

    private static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestConfiguration).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// Το connection string, ή <c>null</c> αν δεν έχει ρυθμιστεί (τα integration tests τότε γίνονται skip).
    /// </summary>
    public static string? ConnectionString
    {
        get
        {
            var value = Configuration.GetConnectionString(ConnectionStringName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static bool HasDatabase
    {
        get { return ConnectionString != null; }
    }

    /// <summary>
    /// Το connection string ή εξαίρεση με οδηγίες — ώστε η αποτυχία να είναι κατανοητή.
    /// </summary>
    public static string RequireConnectionString()
    {
        var value = ConnectionString;
        if (value == null)
        {
            throw new InvalidOperationException(
                "Δεν βρέθηκε connection string 'RetroTools'. Όρισέ το με:\r\n" +
                "  dotnet user-secrets set \"ConnectionStrings:RetroTools\" \"...\" --project src/RetroTools.Web\r\n" +
                "ή με environment variable ConnectionStrings__RetroTools.");
        }

        return value;
    }
}
