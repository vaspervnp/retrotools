using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace RetroTools.Data;

public static class DependencyInjection
{
    /// <summary>
    /// Η έκδοση του server δηλώνεται ρητά αντί για <c>AutoDetect</c>: το autodetect
    /// ανοίγει σύνδεση κατά το startup, που κάνει την εφαρμογή να μην ξεκινά όταν
    /// η βάση είναι προσωρινά κάτω.
    /// </summary>
    public static readonly ServerVersion MariaDb11 = new MariaDbServerVersion(new Version(11, 4));

    public static IServiceCollection AddRetroToolsData(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Το connection string δεν μπορεί να είναι κενό.", nameof(connectionString));
        }

        services.AddDbContext<RetroToolsDbContext>(options =>
        {
            options.UseMySql(connectionString, MariaDb11, mysql =>
            {
                // Η βάση είναι απομακρυσμένη· οι στιγμιαίες διακοπές δικτύου δεν
                // πρέπει να καταλήγουν σε χαμένη δουλειά του χρήστη.
                mysql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                mysql.MigrationsAssembly(typeof(RetroToolsDbContext).Assembly.FullName);
            });

            options.ConfigureWarnings(warnings =>
            {
                // Τα φίλτρα ιδιοκτησίας δηλώνονται και στα παιδιά και στους γονείς,
                // οπότε η προειδοποίηση για required navigation δεν ισχύει εδώ:
                // ένα παιδί δεν μπορεί ποτέ να είναι ορατό χωρίς τον γονέα του.
                warnings.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning);
            });
        });

        return services;
    }
}
