using Microsoft.EntityFrameworkCore;
using RetroTools.Data;
using RetroTools.Data.Seeding;

namespace RetroTools.Web.Configuration;

public static class SeedingExtensions
{
    /// <summary>
    /// Συγχρονίζει τους lookup πίνακες με τον <c>PlatformCatalog</c>.
    /// Τρέχει με <see cref="SystemUser"/> ώστε να παρακάμπτει τα φίλτρα ιδιοκτησίας.
    /// </summary>
    public static async Task SeedPlatformCatalogAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RetroToolsDbContext>>();
        await using var context = new RetroToolsDbContext(options, SystemUser.Instance);

        var pending = await context.Database.GetPendingMigrationsAsync();
        var pendingList = pending.ToList();

        if (pendingList.Count > 0)
        {
            // Δεν εφαρμόζουμε migrations αυτόματα: σε production θα ήταν επικίνδυνο
            // και σε shared βάση θα δημιουργούσε συνθήκες ανταγωνισμού.
            app.Logger.LogWarning(
                "Εκκρεμούν {Count} migrations ({Names}). Τρέξε: dotnet ef database update",
                pendingList.Count,
                string.Join(", ", pendingList));
            return;
        }

        var changes = await PlatformSeeder.SeedAsync(context);

        app.Logger.LogInformation(
            "Ο κατάλογος πλατφορμών συγχρονίστηκε ({Changes} νέες εγγραφές).",
            changes);
    }
}
