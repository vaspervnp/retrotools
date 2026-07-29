using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetroTools.Data;

namespace RetroTools.Web.Configuration;

/// <summary>
/// Χρησιμοποιείται μόνο από τα εργαλεία <c>dotnet ef</c>. Διαβάζει το connection string
/// από τις ίδιες πηγές με την εφαρμογή, ώστε να μη χρειάζεται ποτέ να γραφτεί
/// σε αρχείο ή σε γραμμή εντολών.
/// </summary>
public sealed class RetroToolsDbContextFactory : IDesignTimeDbContextFactory<RetroToolsDbContext>
{
    public RetroToolsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddUserSecrets<RetroToolsDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = ConnectionStringProvider.Require(configuration);

        var options = new DbContextOptionsBuilder<RetroToolsDbContext>()
            .UseMySql(connectionString, DependencyInjection.MariaDb11, mysql =>
                mysql.MigrationsAssembly(typeof(RetroToolsDbContext).Assembly.FullName))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;

        // Τα migrations δεν ανήκουν σε χρήστη — παρακάμπτουν τα φίλτρα ιδιοκτησίας.
        return new RetroToolsDbContext(options, SystemUser.Instance);
    }
}
