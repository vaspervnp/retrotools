using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RetroTools.Web.Tests;

/// <summary>
/// Έλεγχοι πρόσβασης πάνω από την πραγματική HTTP pipeline.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApiAuthorizationTests
{
    private readonly RetroToolsApiFactory _factory;

    public ApiAuthorizationTests(RetroToolsApiFactory factory)
    {
        _factory = factory;
    }

    private static readonly JsonSerializerOptions Json =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private async Task<long> CreateProjectAsync(HttpClient client, string name = "Δοκιμή")
    {
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name,
            modeCode = "cpc.mode0",
        });

        response.EnsureSuccessStatusCode();

        var project = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return project.GetProperty("id").GetInt64();
    }

    // --- Δημόσια endpoints ---------------------------------------------------

    [Fact]
    public async Task Platform_catalog_is_public()
    {
        using var client = _factory.CreateClientAs(null);

        var response = await client.GetAsync("/api/platforms");
        response.EnsureSuccessStatusCode();

        var platforms = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal(3, platforms.GetArrayLength());

        var cpc = platforms.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "cpc");

        Assert.Equal(27, cpc.GetProperty("palette").GetProperty("colorCount").GetInt32());
        Assert.True(cpc.GetProperty("hasProgrammablePalette").GetBoolean());
        Assert.False(cpc.GetProperty("hasHardwareSprites").GetBoolean());
    }

    [Fact]
    public async Task Mode_metadata_includes_the_pixel_slot_roles()
    {
        using var client = _factory.CreateClientAs(null);

        var mode = await client.GetFromJsonAsync<JsonElement>("/api/platforms/modes/c64.sprite_multicolor", Json);
        var slots = mode.GetProperty("pixelSlots").EnumerateArray().ToList();

        Assert.Equal(4, slots.Count);
        Assert.True(slots[1].GetProperty("isGlobal").GetBoolean());
        Assert.True(slots[3].GetProperty("isGlobal").GetBoolean());
        Assert.False(slots[2].GetProperty("isGlobal").GetBoolean());
        Assert.Equal("$D025", slots[1].GetProperty("hardwareRegister").GetString());
    }

    // --- Απαίτηση σύνδεσης ---------------------------------------------------

    /// <summary>
    /// Τα API endpoints πρέπει να επιστρέφουν 401, όχι ανακατεύθυνση 302 σε HTML
    /// σελίδα σύνδεσης — ένα fetch() δεν μπορεί να κάνει τίποτα με redirect.
    /// </summary>
    [DatabaseFact]
    public async Task Anonymous_requests_to_the_api_get_401_not_a_redirect()
    {
        using var client = _factory.CreateClientAs(null);

        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Creating_a_project_requires_authentication()
    {
        using var client = _factory.CreateClientAs(null);

        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Χ", modeCode = "cpc.mode0" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Απομόνωση μεταξύ χρηστών --------------------------------------------

    /// <summary>
    /// Ξένο αντικείμενο δίνει <b>404</b>, όχι 403: ένα 403 θα επιβεβαίωνε ότι το
    /// project υπάρχει, επιτρέποντας απαρίθμηση των ids των άλλων χρηστών.
    /// </summary>
    [DatabaseFact]
    public async Task Foreign_project_returns_404_not_403()
    {
        var alice = await _factory.CreateUserAsync("Alice");
        var bob = await _factory.CreateUserAsync("Bob");

        using var aliceClient = _factory.CreateClientAs(alice);
        using var bobClient = _factory.CreateClientAs(bob);

        var projectId = await CreateProjectAsync(aliceClient, "Της Alice");

        var get = await bobClient.GetAsync("/api/projects/" + projectId);
        var delete = await bobClient.DeleteAsync("/api/projects/" + projectId);
        var put = await bobClient.PutAsJsonAsync("/api/projects/" + projectId, new
        {
            name = "Κλεμμένο",
            visibility = 0,
        });

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [DatabaseFact]
    public async Task Listing_shows_only_my_projects()
    {
        var alice = await _factory.CreateUserAsync("Alice");
        var bob = await _factory.CreateUserAsync("Bob");

        using var aliceClient = _factory.CreateClientAs(alice);
        using var bobClient = _factory.CreateClientAs(bob);

        await CreateProjectAsync(aliceClient, "Της Alice");

        var bobProjects = await bobClient.GetFromJsonAsync<JsonElement>("/api/projects", Json);

        Assert.Equal(0, bobProjects.GetArrayLength());
    }

    /// <summary>
    /// Το πιο λεπτό σημείο ολόκληρου του authorization: τα global query filters
    /// αφήνουν τα δημόσια projects να φανούν, που είναι σωστό για ανάγνωση.
    /// Αν όμως η διαδρομή εγγραφής χρησιμοποιούσε το ίδιο ερώτημα, κάθε δημόσιο
    /// project θα γινόταν εγγράψιμο από οποιονδήποτε.
    /// </summary>
    [DatabaseFact]
    public async Task Public_project_is_readable_by_others_but_never_writable()
    {
        var alice = await _factory.CreateUserAsync("Alice");
        var bob = await _factory.CreateUserAsync("Bob");

        using var aliceClient = _factory.CreateClientAs(alice);
        using var bobClient = _factory.CreateClientAs(bob);
        using var anonymous = _factory.CreateClientAs(null);

        var projectId = await CreateProjectAsync(aliceClient, "Δημόσιο");

        var publish = await aliceClient.PutAsJsonAsync("/api/projects/" + projectId, new
        {
            name = "Δημόσιο",
            visibility = 2,
        });
        publish.EnsureSuccessStatusCode();

        // Ανάγνωση: επιτρέπεται σε όλους.
        var bobRead = await bobClient.GetAsync("/api/projects/" + projectId);
        var anonymousRead = await anonymous.GetAsync("/api/projects/" + projectId);

        Assert.Equal(HttpStatusCode.OK, bobRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, anonymousRead.StatusCode);

        var dto = await bobRead.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(dto.GetProperty("isOwner").GetBoolean());

        // Εγγραφή: απαγορεύεται, ακόμη κι αν το project είναι ορατό.
        var bobWrite = await bobClient.PutAsJsonAsync("/api/projects/" + projectId, new
        {
            name = "Καταπατημένο",
            visibility = 2,
        });

        var bobDelete = await bobClient.DeleteAsync("/api/projects/" + projectId);

        Assert.Equal(HttpStatusCode.NotFound, bobWrite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobDelete.StatusCode);

        // Και το όνομα δεν άλλαξε.
        var after = await aliceClient.GetFromJsonAsync<JsonElement>("/api/projects/" + projectId, Json);
        Assert.Equal("Δημόσιο", after.GetProperty("name").GetString());
    }

    [DatabaseFact]
    public async Task Sprites_of_a_foreign_project_are_not_reachable()
    {
        var alice = await _factory.CreateUserAsync("Alice");
        var bob = await _factory.CreateUserAsync("Bob");

        using var aliceClient = _factory.CreateClientAs(alice);
        using var bobClient = _factory.CreateClientAs(bob);

        var projectId = await CreateProjectAsync(aliceClient);

        var created = await aliceClient.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
        {
            name = "Μυστικό",
            widthPx = 16,
            heightPx = 16,
        });
        created.EnsureSuccessStatusCode();

        var sprite = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var spriteId = sprite.GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.GetAsync("/api/sprites/" + spriteId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.GetAsync("/api/sprites/" + spriteId + "/frames/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.GetAsync("/api/projects/" + projectId + "/sprites")).StatusCode);
    }
}
