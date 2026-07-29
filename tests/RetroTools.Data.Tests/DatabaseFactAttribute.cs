namespace RetroTools.Data.Tests;

/// <summary>
/// Test που απαιτεί ζωντανή MariaDB. Αν δεν έχει ρυθμιστεί connection string
/// (π.χ. σε CI χωρίς secrets) το test γίνεται skip αντί για fail.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!TestConfiguration.HasDatabase)
        {
            Skip = "Δεν έχει ρυθμιστεί το connection string 'RetroTools' (user secrets ή ConnectionStrings__RetroTools).";
        }
    }
}
