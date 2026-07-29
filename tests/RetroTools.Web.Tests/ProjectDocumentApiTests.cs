using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RetroTools.Web.Tests;

[Collection(ApiCollection.Name)]
public class ProjectDocumentApiTests
{
    private readonly RetroToolsApiFactory _factory;

    private static readonly JsonSerializerOptions Json =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ProjectDocumentApiTests(RetroToolsApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Στήνει project με ομάδα, δύο sprites με σχεδιασμένα pixels, και spritemap.</summary>
    private async Task<(HttpClient Client, long ProjectId)> BuildProjectAsync(string name = "Πηγή")
    {
        var userId = await _factory.CreateUserAsync("Εξαγωγέας");
        var client = _factory.CreateClientAs(userId);

        var projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            name,
            modeCode = "cpc.mode0",
        });
        projectResponse.EnsureSuccessStatusCode();

        var projectId = (await projectResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetInt64();

        for (var i = 0; i < 2; i++)
        {
            var spriteResponse = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
            {
                name = "sprite" + i,
                widthPx = 8,
                heightPx = 8,
            });
            spriteResponse.EnsureSuccessStatusCode();

            var spriteId = (await spriteResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("id").GetInt64();

            var pixels = new byte[64];
            for (var p = 0; p < pixels.Length; p++)
            {
                pixels[p] = (byte)((p + i) % 16);
            }

            var save = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                durationMs = 50 + i,
                pixels = Convert.ToBase64String(pixels),
            });
            save.EnsureSuccessStatusCode();
        }

        return (client, projectId);
    }

    private static MultipartFormDataContent MakeUpload(byte[] content, string fileName = "project.json")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", fileName);

        return form;
    }

    // --- Εξαγωγή -------------------------------------------------------------

    [DatabaseFact]
    public async Task Export_returns_a_json_document_with_the_project_contents()
    {
        var (client, projectId) = await BuildProjectAsync();
        using (client)
        {
            var response = await client.GetAsync("/api/projects/" + projectId + "/document");
            response.EnsureSuccessStatusCode();

            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Πηγή", response.Content.Headers.ContentDisposition?.FileNameStar ?? string.Empty, StringComparison.Ordinal);

            var document = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

            Assert.Equal("retrotools-project", document.GetProperty("format").GetString());
            Assert.Equal(1, document.GetProperty("version").GetInt32());
            Assert.Equal("cpc.mode0", document.GetProperty("modeCode").GetString());
            Assert.Equal(2, document.GetProperty("sprites").GetArrayLength());
            Assert.Equal(16, document.GetProperty("palette").GetArrayLength());
        }
    }

    [DatabaseFact]
    public async Task Foreign_project_cannot_be_exported()
    {
        var (owner, projectId) = await BuildProjectAsync();
        var intruderId = await _factory.CreateUserAsync("Εισβολέας");

        using (owner)
        using (var intruder = _factory.CreateClientAs(intruderId))
        {
            var response = await intruder.GetAsync("/api/projects/" + projectId + "/document");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // --- Πλήρες round trip ---------------------------------------------------

    /// <summary>
    /// Η ουσία της λειτουργίας: ό,τι κατέβηκε πρέπει να ξαναγίνει το ίδιο project,
    /// μέχρι και τα pixels του κάθε καρέ.
    /// </summary>
    [DatabaseFact]
    public async Task Round_trip_recreates_the_project_with_identical_pixels()
    {
        var (client, projectId) = await BuildProjectAsync("Αρχικό");
        using (client)
        {
            var original = await client.GetFromJsonAsync<JsonElement>(
                "/api/projects/" + projectId + "/sprites", Json);
            var originalFrame = await client.GetFromJsonAsync<JsonElement>(
                "/api/sprites/" + original[0].GetProperty("id").GetInt64() + "/frames/0", Json);

            var exported = await (await client.GetAsync("/api/projects/" + projectId + "/document"))
                .Content.ReadAsByteArrayAsync();

            var import = await client.PostAsync("/api/projects/import?name=Αντίγραφο", MakeUpload(exported));
            import.EnsureSuccessStatusCode();

            var summary = await import.Content.ReadFromJsonAsync<JsonElement>(Json);
            var newProjectId = summary.GetProperty("id").GetInt64();

            Assert.NotEqual(projectId, newProjectId);
            Assert.Equal("Αντίγραφο", summary.GetProperty("name").GetString());
            Assert.Equal(2, summary.GetProperty("sprites").GetInt32());

            var copiedSprites = await client.GetFromJsonAsync<JsonElement>(
                "/api/projects/" + newProjectId + "/sprites", Json);

            Assert.Equal(2, copiedSprites.GetArrayLength());
            Assert.Equal("sprite0", copiedSprites[0].GetProperty("name").GetString());

            var copiedFrame = await client.GetFromJsonAsync<JsonElement>(
                "/api/sprites/" + copiedSprites[0].GetProperty("id").GetInt64() + "/frames/0", Json);

            Assert.Equal(
                originalFrame.GetProperty("pixels").GetString(),
                copiedFrame.GetProperty("pixels").GetString());
            Assert.Equal(
                originalFrame.GetProperty("durationMs").GetInt32(),
                copiedFrame.GetProperty("durationMs").GetInt32());
        }
    }

    /// <summary>
    /// Η εισαγωγή δημιουργεί πάντα νέο project και <b>δεν</b> αγγίζει το αρχικό —
    /// ούτε καν όταν το αρχείο προέρχεται από αυτό.
    /// </summary>
    [DatabaseFact]
    public async Task Import_never_overwrites_the_source_project()
    {
        var (client, projectId) = await BuildProjectAsync("Ανέπαφο");
        using (client)
        {
            var exported = await (await client.GetAsync("/api/projects/" + projectId + "/document"))
                .Content.ReadAsByteArrayAsync();

            await client.PostAsync("/api/projects/import", MakeUpload(exported));
            await client.PostAsync("/api/projects/import", MakeUpload(exported));

            var projects = await client.GetFromJsonAsync<JsonElement>("/api/projects", Json);

            Assert.Equal(3, projects.GetArrayLength());

            var source = await client.GetFromJsonAsync<JsonElement>("/api/projects/" + projectId, Json);
            Assert.Equal("Ανέπαφο", source.GetProperty("name").GetString());
        }
    }

    /// <summary>
    /// Ο ιδιοκτήτης του εισαγόμενου project είναι πάντα αυτός που ανεβάζει.
    /// Ένα αρχείο δεν μπορεί να «χαρίσει» δεδομένα σε άλλον λογαριασμό.
    /// </summary>
    [DatabaseFact]
    public async Task Imported_project_belongs_to_the_uploader()
    {
        var (owner, projectId) = await BuildProjectAsync();
        var otherId = await _factory.CreateUserAsync("Παραλήπτης");

        using (owner)
        using (var other = _factory.CreateClientAs(otherId))
        {
            var exported = await (await owner.GetAsync("/api/projects/" + projectId + "/document"))
                .Content.ReadAsByteArrayAsync();

            var import = await other.PostAsync("/api/projects/import", MakeUpload(exported));
            import.EnsureSuccessStatusCode();

            var newId = (await import.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("id").GetInt64();

            // Ο παραλήπτης το βλέπει· ο αρχικός ιδιοκτήτης όχι.
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/projects/" + newId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/projects/" + newId)).StatusCode);
        }
    }

    // --- Απόρριψη κακών αρχείων ---------------------------------------------

    [DatabaseFact]
    public async Task Import_requires_authentication()
    {
        using var anonymous = _factory.CreateClientAs(null);

        var response = await anonymous.PostAsync(
            "/api/projects/import",
            MakeUpload(Encoding.UTF8.GetBytes("{}")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Import_rejects_a_file_that_is_not_a_retrotools_project()
    {
        var userId = await _factory.CreateUserAsync("Δοκιμαστής");
        using var client = _factory.CreateClientAs(userId);

        var response = await client.PostAsync(
            "/api/projects/import",
            MakeUpload(Encoding.UTF8.GetBytes("{\"hello\":\"world\"}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("format", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Import_rejects_a_tampered_document_and_creates_nothing()
    {
        var (client, projectId) = await BuildProjectAsync();
        using (client)
        {
            var exported = await (await client.GetAsync("/api/projects/" + projectId + "/document"))
                .Content.ReadAsByteArrayAsync();

            // Αλλοιώνουμε το mode σε ένα που απαιτεί άλλες διαστάσεις sprite.
            var tampered = Encoding.UTF8.GetString(exported)
                .Replace("\"cpc.mode0\"", "\"c64.sprite_hires\"", StringComparison.Ordinal);

            var before = (await client.GetFromJsonAsync<JsonElement>("/api/projects", Json)).GetArrayLength();

            var response = await client.PostAsync(
                "/api/projects/import",
                MakeUpload(Encoding.UTF8.GetBytes(tampered)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var after = (await client.GetFromJsonAsync<JsonElement>("/api/projects", Json)).GetArrayLength();
            Assert.Equal(before, after);
        }
    }

    [DatabaseFact]
    public async Task Import_without_a_file_explains_what_is_missing()
    {
        var userId = await _factory.CreateUserAsync("Δοκιμαστής");
        using var client = _factory.CreateClientAs(userId);

        var response = await client.PostAsync("/api/projects/import", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
