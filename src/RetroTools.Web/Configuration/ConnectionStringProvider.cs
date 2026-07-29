namespace RetroTools.Web.Configuration;

/// <summary>
/// Ενιαίο σημείο ανάγνωσης του connection string, με fail-fast και κατανοητό μήνυμα.
/// Το connection string ΔΕΝ βρίσκεται ποτέ σε committed αρχείο.
/// </summary>
public static class ConnectionStringProvider
{
    public const string Name = "RetroTools";

    public static string Require(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString(Name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Λείπει το connection string 'ConnectionStrings:RetroTools'." + Environment.NewLine +
                "Όρισέ το με έναν από τους παρακάτω τρόπους:" + Environment.NewLine +
                "  1) dotnet user-secrets set \"ConnectionStrings:RetroTools\" \"Server=...;Port=3306;Database=DB_NAME;User ID=...;Password=...;\" --project src/RetroTools.Web" + Environment.NewLine +
                "  2) environment variable: ConnectionStrings__RetroTools" + Environment.NewLine +
                "  3) appsettings.Local.json (δες το appsettings.Local.json.example)" + Environment.NewLine +
                "Μην το βάλεις ΠΟΤΕ σε αρχείο που μπαίνει στο git.");
        }

        return value;
    }
}
