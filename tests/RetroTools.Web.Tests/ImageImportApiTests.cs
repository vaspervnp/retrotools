using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RetroTools.Core.Imaging;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Web.Tests;

[Collection(ApiCollection.Name)]
public class ImageImportApiTests
{
    private readonly RetroToolsApiFactory _factory;

    private static readonly JsonSerializerOptions Json =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ImageImportApiTests(RetroToolsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, long ProjectId)> NewProjectAsync(string modeCode)
    {
        var userId = await _factory.CreateUserAsync("Εισαγωγέας");
        var client = _factory.CreateClientAs(userId);

        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Import", modeCode });
        response.EnsureSuccessStatusCode();

        var projectId = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetInt64();

        return (client, projectId);
    }

    /// <summary>Φτιάχνει PNG με χρώματα υλικού της πλατφόρμας.</summary>
    private static byte[] MakePng(string platformCode, int width, int height, Func<int, int, int> hardwareColor)
    {
        var platform = PlatformCatalog.Get(platformCode);
        var palette = new List<Rgb24>();
        var lookup = new Dictionary<Rgb24, byte>();
        var frame = new FrameBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rgb = platform.Palette.GetRgb(hardwareColor(x, y));

                if (!lookup.TryGetValue(rgb, out var index))
                {
                    index = (byte)palette.Count;
                    palette.Add(rgb);
                    lookup[rgb] = index;
                }

                frame[x, y] = index;
            }
        }

        return PngWriter.WriteIndexed(frame, palette);
    }

    private static MultipartFormDataContent Upload(byte[] content, string fileName = "sprite.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);

        return form;
    }

    // --- Επιτυχής εισαγωγή ---------------------------------------------------

    [DatabaseFact]
    public async Task Png_becomes_a_sprite_with_the_image_dimensions()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var png = MakePng("cpc", 16, 16, (x, y) => x < 8 ? 0 : 6);

            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png?name=εισαγμένο",
                Upload(png));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

            Assert.Equal("εισαγμένο", result.GetProperty("name").GetString());
            Assert.Equal(16, result.GetProperty("width").GetInt32());
            Assert.Equal(16, result.GetProperty("height").GetInt32());
            Assert.Empty(result.GetProperty("warnings").EnumerateArray());
        }
    }

    /// <summary>
    /// Η ουσία: τα χρώματα της εικόνας πρέπει να ξαναβγαίνουν αυτούσια όταν
    /// υπάρχουν στην παλέτα του υλικού.
    /// </summary>
    [DatabaseFact]
    public async Task Imported_pixels_preserve_the_structure_of_the_image()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var png = MakePng("cpc", 8, 2, (x, y) => x < 4 ? 0 : 6);

            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(png));
            response.EnsureSuccessStatusCode();

            var spriteId = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("id").GetInt64();

            var frame = await client.GetFromJsonAsync<JsonElement>(
                "/api/sprites/" + spriteId + "/frames/0", Json);
            var pixels = Convert.FromBase64String(frame.GetProperty("pixels").GetString()!);

            Assert.Equal(16, pixels.Length);
            Assert.Equal(pixels[0], pixels[3]);
            Assert.Equal(pixels[4], pixels[7]);
            Assert.NotEqual(pixels[0], pixels[4]);
        }
    }

    [DatabaseFact]
    public async Task Sprite_name_falls_back_to_the_file_name()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(MakePng("cpc", 8, 8, (x, y) => 0), "hero_walk.png"));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal("hero_walk", result.GetProperty("name").GetString());
        }
    }

    // --- Προειδοποιήσεις -----------------------------------------------------

    [DatabaseFact]
    public async Task Reports_colour_loss_when_the_image_is_too_rich()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode1");
        using (client)
        {
            var png = MakePng("cpc", 8, 4, (x, y) => x * 3);

            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(png));

            response.EnsureSuccessStatusCode();

            var warnings = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();

            Assert.NotEmpty(warnings);
            Assert.Contains(warnings, w => w.Contains("slots", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Στο Spectrum ένα κελί 8×8 χωράει δύο χρώματα. Ο χρήστης πρέπει να μάθει
    /// τι χάθηκε κατά τη μετατροπή, με αριθμούς.
    /// </summary>
    [DatabaseFact]
    public async Task Reports_attribute_clash_losses_for_the_spectrum()
    {
        var (client, projectId) = await NewProjectAsync("zx.sprite");
        using (client)
        {
            var png = MakePng("zx", 8, 8, (x, y) => (x / 2) % 4 * 4);

            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(png));

            response.EnsureSuccessStatusCode();

            var warnings = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();

            Assert.Contains(warnings, w => w.Contains("attribute clash", StringComparison.Ordinal));
        }
    }

    // --- Απορρίψεις ----------------------------------------------------------

    [DatabaseFact]
    public async Task Rejects_dimensions_the_mode_cannot_produce()
    {
        var (client, projectId) = await NewProjectAsync("c64.sprite_hires");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(MakePng("c64", 16, 16, (x, y) => 0)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("detail").GetString()!;

            Assert.Contains("16×16", detail, StringComparison.Ordinal);
            Assert.Contains("24×21", detail, StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Rejects_a_file_that_is_not_a_png()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(Encoding.ASCII.GetBytes("this is not a png at all")));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("detail").GetString()!;

            Assert.Contains("PNG", detail, StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Cannot_import_into_a_foreign_project()
    {
        var (owner, projectId) = await NewProjectAsync("cpc.mode0");
        var intruderId = await _factory.CreateUserAsync("Εισβολέας");

        using (owner)
        using (var intruder = _factory.CreateClientAs(intruderId))
        {
            var response = await intruder.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(MakePng("cpc", 8, 8, (x, y) => 0)));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [DatabaseFact]
    public async Task Import_requires_authentication()
    {
        var (owner, projectId) = await NewProjectAsync("cpc.mode0");
        using (owner)
        using (var anonymous = _factory.CreateClientAs(null))
        {
            var response = await anonymous.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(MakePng("cpc", 8, 8, (x, y) => 0)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // --- Στρατηγική παλέτας --------------------------------------------------

    [DatabaseFact]
    public async Task Keep_palette_leaves_the_project_colours_untouched()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png?keepPalette=true",
                Upload(MakePng("cpc", 8, 8, (x, y) => 15)));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.False(result.GetProperty("paletteChanged").GetBoolean());
        }
    }

    [DatabaseFact]
    public async Task Auto_palette_reports_that_it_changed_the_project_colours()
    {
        var (client, projectId) = await NewProjectAsync("cpc.mode0");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/projects/" + projectId + "/sprites/import-png",
                Upload(MakePng("cpc", 8, 8, (x, y) => 21)));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.True(result.GetProperty("paletteChanged").GetBoolean());
        }
    }
}
