using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RetroTools.Web.Tests;

[Collection(ApiCollection.Name)]
public class ExportApiTests
{
    private readonly RetroToolsApiFactory _factory;

    private static readonly JsonSerializerOptions Json =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ExportApiTests(RetroToolsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, long SpriteId)> NewSpriteAsync(
        string modeCode,
        int width,
        int height,
        byte[]? pixels = null)
    {
        var userId = await _factory.CreateUserAsync("Εξαγωγέας");
        var client = _factory.CreateClientAs(userId);

        var projectResponse = await client.PostAsJsonAsync("/api/projects", new { name = "Export", modeCode });
        projectResponse.EnsureSuccessStatusCode();
        var projectId = (await projectResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetInt64();

        var spriteResponse = await client.PostAsJsonAsync("/api/projects/" + projectId + "/sprites", new
        {
            name = "player",
            widthPx = width,
            heightPx = height,
        });
        spriteResponse.EnsureSuccessStatusCode();
        var spriteId = (await spriteResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetInt64();

        if (pixels != null)
        {
            var save = await client.PutAsJsonAsync("/api/sprites/" + spriteId + "/frames/0", new
            {
                pixels = Convert.ToBase64String(pixels),
            });
            save.EnsureSuccessStatusCode();
        }

        return (client, spriteId);
    }

    [DatabaseFact]
    public async Task Format_list_is_filtered_by_platform()
    {
        var (client, spriteId) = await NewSpriteAsync("c64.sprite_hires", 24, 21);
        using (client)
        {
            var formats = await client.GetFromJsonAsync<JsonElement>(
                "/api/export/sprite/" + spriteId + "/formats", Json);

            var ids = formats.EnumerateArray().Select(f => f.GetProperty("id").GetString()).ToList();

            Assert.Contains("asm-6502", ids);
            Assert.Contains("prg", ids);
            Assert.DoesNotContain("asm-z80", ids);
        }
    }

    /// <summary>
    /// Το κρίσιμο: τα bytes που κατεβάζει ο χρήστης πρέπει να είναι ακριβώς
    /// αυτά που περιμένει το VIC-II — 63, με τα σωστά bits.
    /// </summary>
    [DatabaseFact]
    public async Task C64_binary_export_is_63_bytes_with_the_expected_bits()
    {
        var pixels = new byte[24 * 21];

        // Πρώτη γραμμή γεμάτη, ένα pixel στην τελευταία στήλη της δεύτερης.
        for (var x = 0; x < 24; x++)
        {
            pixels[x] = 1;
        }

        pixels[24 + 23] = 1;

        var (client, spriteId) = await NewSpriteAsync("c64.sprite_hires", 24, 21, pixels);
        using (client)
        {
            var response = await client.GetAsync("/api/export/sprite/" + spriteId + "?format=bin");
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(63, data.Length);
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, data.Take(3).ToArray());
            Assert.Equal(new byte[] { 0x00, 0x00, 0x01 }, data.Skip(3).Take(3).ToArray());
        }
    }

    [DatabaseFact]
    public async Task Prg_export_prepends_the_load_address()
    {
        var (client, spriteId) = await NewSpriteAsync("c64.sprite_hires", 24, 21);
        using (client)
        {
            var response = await client.GetAsync(
                "/api/export/sprite/" + spriteId + "?format=prg&loadAddress=" + 0x3000);

            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(65, data.Length);
            Assert.Equal(0x00, data[0]);
            Assert.Equal(0x30, data[1]);
        }
    }

    [DatabaseFact]
    public async Task Cpc_assembly_export_contains_defb_and_palette_comments()
    {
        var pixels = new byte[16 * 16];
        pixels[0] = 15;

        var (client, spriteId) = await NewSpriteAsync("cpc.mode0", 16, 16, pixels);
        using (client)
        {
            var response = await client.GetAsync("/api/export/sprite/" + spriteId + "?format=asm-z80");
            response.EnsureSuccessStatusCode();

            var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

            Assert.Contains("player:", text, StringComparison.Ordinal);
            // Pen 15 αριστερά, 0 δεξιά: τα τέσσερα bits του pen πάνε στις θέσεις
            // 7, 5, 3, 1 του byte → 0xAA. Αυτό είναι το interleaved encoding του CPC.
            Assert.Contains("defb &AA", text, StringComparison.Ordinal);
            Assert.Contains("player_width_bytes equ 8", text, StringComparison.Ordinal);
            Assert.Contains("hardware &54", text, StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Png_export_is_a_real_png_with_the_pixel_aspect_applied()
    {
        var (client, spriteId) = await NewSpriteAsync("cpc.mode0", 16, 16);
        using (client)
        {
            var response = await client.GetAsync("/api/export/sprite/" + spriteId + "?format=png&scale=2");
            response.EnsureSuccessStatusCode();

            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

            var data = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, data.Take(4).ToArray());
            Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)));
            Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4)));
        }
    }

    [DatabaseFact]
    public async Task Incompatible_format_is_rejected_with_an_explanation()
    {
        var (client, spriteId) = await NewSpriteAsync("cpc.mode0", 16, 16);
        using (client)
        {
            var response = await client.GetAsync("/api/export/sprite/" + spriteId + "?format=prg");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Contains("Mode 0", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Unknown_format_lists_the_available_ones()
    {
        var (client, spriteId) = await NewSpriteAsync("cpc.mode0", 16, 16);
        using (client)
        {
            var response = await client.GetAsync("/api/export/sprite/" + spriteId + "?format=iff");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("detail").GetString()!;

            Assert.Contains("bin", detail, StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Foreign_sprite_cannot_be_exported()
    {
        var (owner, spriteId) = await NewSpriteAsync("cpc.mode0", 16, 16);
        var intruderId = await _factory.CreateUserAsync("Εισβολέας");

        using (owner)
        using (var intruder = _factory.CreateClientAs(intruderId))
        {
            var response = await intruder.GetAsync("/api/export/sprite/" + spriteId + "?format=bin");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
