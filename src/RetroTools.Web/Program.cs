using Microsoft.Extensions.Options;
using RetroTools.Data;
using RetroTools.Web.Components;
using RetroTools.Web.Configuration;

var builder = WebApplication.CreateBuilder(args);

// --- Ρυθμίσεις -------------------------------------------------------------
// Σειρά προτεραιότητας: env vars > appsettings.Local.json > appsettings.{Env}.json > appsettings.json
// Τα secrets (connection string, OAuth) δεν μπαίνουν ποτέ σε committed αρχείο.
builder.Configuration.AddLocalConfiguration(builder.Environment);

builder.Services.AddOptions<RetroToolsOptions>()
    .Bind(builder.Configuration.GetSection(RetroToolsOptions.SectionName));

var hostingOptions = builder.Configuration
    .GetSection(RetroToolsOptions.SectionName)
    .Get<RetroToolsOptions>() ?? new RetroToolsOptions();

// --- Self-hosted ως service (Windows Service ή systemd) ---------------------
// Και οι δύο κλήσεις είναι no-op όταν η εφαρμογή τρέχει από κονσόλα.
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// --- Services --------------------------------------------------------------
builder.Services.ConfigureForwardedHeaders(hostingOptions);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
// Scoped και όχι singleton: το DbContextOptions που καταναλώνει είναι scoped,
// και ένα singleton δεν επιτρέπεται να εξαρτάται από scoped υπηρεσία.
builder.Services.AddScoped<RetroTools.Web.Services.EditorDataService>();
builder.Services.AddSingleton<RetroTools.Web.Services.SpritePreviewService>();
builder.Services.AddScoped<RetroTools.Web.Services.ProjectDocumentService>();

builder.Services.AddProblemDetails();

// Fail-fast: αν λείπει το connection string, σταματάμε εδώ με κατανοητό μήνυμα
// αντί για NullReferenceException στο πρώτο query.
var connectionString = ConnectionStringProvider.Require(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<RetroTools.Web.Services.ProjectAccess>();
builder.Services.AddRetroToolsData(connectionString);

// Οι providers καταχωρούνται μόνο όσοι έχουν κλειδιά· χωρίς αυτά η εφαρμογή
// σηκώνεται κανονικά και απλώς δεν προσφέρει σύνδεση.
var authenticationSettings = builder.AddRetroToolsAuthentication();
builder.Services.AddSingleton(authenticationSettings);

var app = builder.Build();

// Τα δεδομένα υλικού συγχρονίζονται με τον PlatformCatalog σε κάθε εκκίνηση.
await app.SeedPlatformCatalogAsync();

var options = app.Services.GetRequiredService<IOptions<RetroToolsOptions>>().Value;

// --- Pipeline --------------------------------------------------------------
if (options.BehindReverseProxy)
{
    app.UseForwardedHeaders();
}

if (!string.IsNullOrWhiteSpace(options.PathBase))
{
    app.UsePathBase(options.PathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (options.EnableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Logger.LogInformation(
    "RetroTools ξεκίνησε. Database host: {Host}",
    new MySqlConnector.MySqlConnectionStringBuilder(connectionString).Server);

app.Run();

/// <summary>
/// Με top-level statements ο τύπος Program παράγεται ως internal. Δηλώνεται εδώ
/// ρητά ως public ώστε το <c>WebApplicationFactory&lt;Program&gt;</c> των integration
/// tests να μπορεί να τον δει.
/// </summary>
public partial class Program
{
}
