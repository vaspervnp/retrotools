using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RetroTools.Web.Tests;

/// <summary>
/// Το API πρέπει να απορρίπτει ό,τι δεν μπορεί να υπάρξει στο πραγματικό υλικό.
/// Αν περάσει, το λάθος θα εμφανιστεί πολύ αργότερα — στο export ή στον emulator.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApiValidationTests
{
    private readonly RetroToolsApiFactory _factory;

    private static readonly JsonSerializerOptions Json =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ApiValidationTests(RetroToolsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, long ProjectId)> NewProjectAsync(string modeCode)
    {
        var userId = await _factory.CreateUserAsync("Δοκιμαστής");
        var client = _factory.CreateClientAs(userId);

        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Δοκιμή", modeCode });
        response.EnsureSuccessStatusCode();

        var project = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return (client, project.GetProperty("id").GetInt64());
    }

    private static async Task<long> CreateSpriteAsync(HttpClient client, long projectId, int width, int height)
    {
        var response = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
        {
            name = "Sprite",
            widthPx = width,
            heightPx = height,
        });

        response.EnsureSuccessStatusCode();

        var sprite = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return sprite.GetProperty("id").GetInt64();
    }

    // --- Projects ------------------------------------------------------------

    [DatabaseFact]
    public async Task Unknown_mode_is_rejected_with_a_helpful_message()
    {
        var userId = await _factory.CreateUserAsync("Δοκιμαστής");
        using var client = _factory.CreateClientAs(userId);

        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Λάθος",
            modeCode = "amiga.aga",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("/api/platforms", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    /// <summary>Το mode συνεπάγεται την πλατφόρμα, οπότε δεν μπορούν να έρθουν σε αντίφαση.</summary>
    [DatabaseFact]
    public async Task Platform_is_derived_from_the_mode()
    {
        var (client, projectId) = await NewProjectAsync("c64.sprite_hires");
        using (client)
        {
            var project = await client.GetFromJsonAsync<JsonElement>("/api/projects/" + projectId, Json);

            Assert.Equal("c64", project.GetProperty("platformCode").GetString());
            Assert.Equal("pepto", project.GetProperty("paletteProfileId").GetString());
        }
    }

    // --- Διαστάσεις sprite ---------------------------------------------------

    /// <summary>
    /// Το C64 hardware sprite είναι καρφωμένο στα 24×21 από το VIC-II.
    /// Οτιδήποτε άλλο απλώς δεν υπάρχει.
    /// </summary>
    [DatabaseFact]
    public async Task C64_hardware_sprite_size_is_enforced()
    {
        var (client, projectId) = await NewProjectAsync("c64.sprite_hires");
        using (client)
        {
            var wrong = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
            {
                name = "Λάθος μέγεθος",
                widthPx = 16,
                heightPx = 16,
            });

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

            var problem = await wrong.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Contains("24×21", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

            var correct = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
            {
                name = "Σωστό",
                widthPx = 24,
                heightPx = 21,
            });

            Assert.Equal(HttpStatusCode.Created, correct.StatusCode);
        }
    }

    /// <summary>Στο CPC Mode 1 τέσσερα pixels μοιράζονται ένα byte — πλάτος 6 δεν αποθηκεύεται.</summary>
    [DatabaseFact]
    public async Task Cpc_mode1_requires_width_divisible_by_four()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode1");
        using (client)
        {
            var wrong = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
            {
                name = "Λάθος",
                widthPx = 6,
                heightPx = 16,
            });

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

            var problem = await wrong.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Contains("πολλαπλάσιο του 4", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task New_sprite_starts_with_one_empty_frame()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 16, 16);

            var frames = await client.GetFromJsonAsync<JsonElement>("/api/sprites/" + spriteId + "/frames", Json);

            Assert.Equal(1, frames.GetArrayLength());

            var pixels = Convert.FromBase64String(frames[0].GetProperty("pixels").GetString()!);

            Assert.Equal(256, pixels.Length);
            Assert.All(pixels, p => Assert.Equal(0, p));
        }
    }

    // --- Δεδομένα καρέ -------------------------------------------------------

    [DatabaseFact]
    public async Task Frame_round_trips_through_the_api()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 8, 8);

            var pixels = new byte[64];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i % 16);
            }

            var save = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                durationMs = 120,
                pixels = Convert.ToBase64String(pixels),
            });

            save.EnsureSuccessStatusCode();

            var loaded = await client.GetFromJsonAsync<JsonElement>("/api/sprites/" + spriteId + "/frames/0", Json);

            Assert.Equal(120, loaded.GetProperty("durationMs").GetInt32());
            Assert.Equal(pixels, Convert.FromBase64String(loaded.GetProperty("pixels").GetString()!));
        }
    }

    [DatabaseFact]
    public async Task Frame_with_the_wrong_pixel_count_is_rejected()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 8, 8);

            var response = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(new byte[10]),
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Contains("64 bytes", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Το CPC Mode 1 έχει μόνο 4 pens. Χωρίς αυτόν τον έλεγχο, το pen 7 θα
    /// αποθηκευόταν και θα «ξεχείλιζε» σε γειτονικά pixels κατά το packing.
    /// </summary>
    [DatabaseFact]
    public async Task Colour_index_above_the_mode_limit_is_rejected()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode1");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 8, 8);

            var pixels = new byte[64];
            pixels[5] = 7;

            var response = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(pixels),
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("detail").GetString()!;

            Assert.Contains("0–3", detail, StringComparison.Ordinal);
            Assert.Contains("5,0", detail, StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Malformed_base64_gets_a_clear_message()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 8, 8);

            var response = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = "αυτό δεν είναι base64",
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    /// <summary>Ένα sprite χωρίς κανένα καρέ θα άνοιγε άδειο στον editor.</summary>
    [DatabaseFact]
    public async Task The_last_frame_cannot_be_deleted()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 8, 8);

            var response = await client.DeleteAsync("/api/sprites/" + spriteId + "/frames/0");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    // --- ZX attributes -------------------------------------------------------

    [DatabaseFact]
    public async Task Zx_attributes_must_match_the_cell_grid()
    {
        var (client, projectId) = await NewProjectAsync("zx.sprite");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 16, 16);

            // 16×16 pixels → 2×2 κελιά των 8×8 → 4 attribute bytes.
            var wrong = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(new byte[256]),
                attributes = Convert.ToBase64String(new byte[3]),
            });

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Contains(
                "4 bytes",
                (await wrong.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("detail").GetString(),
                StringComparison.Ordinal);

            var correct = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(new byte[256]),
                attributes = Convert.ToBase64String(new byte[] { 0x47, 0x38, 0x07, 0x00 }),
            });

            correct.EnsureSuccessStatusCode();

            var loaded = await client.GetFromJsonAsync<JsonElement>("/api/sprites/" + spriteId + "/frames/0", Json);

            Assert.Equal(
                new byte[] { 0x47, 0x38, 0x07, 0x00 },
                Convert.FromBase64String(loaded.GetProperty("attributes").GetString()!));
        }
    }

    /// <summary>Ο CPC δεν έχει attributes — μια τέτοια αίτηση είναι σφάλμα, όχι σιωπηλή αγνόηση.</summary>
    [DatabaseFact]
    public async Task Attributes_are_rejected_for_modes_that_do_not_use_them()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var spriteId = await CreateSpriteAsync(client, projectId, 16, 16);

            var response = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(new byte[256]),
                attributes = Convert.ToBase64String(new byte[4]),
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
