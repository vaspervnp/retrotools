using System.Text;
using RetroTools.Core.Serialization;

namespace RetroTools.Core.Tests;

public class ProjectDocumentTests
{
    private static ProjectDocument ValidDocument()
    {
        var document = new ProjectDocument
        {
            // Τα δύο πεδία του «φακέλου» τα σφραγίζει κανονικά ο serializer· εδώ
            // μπαίνουν ρητά γιατί αρκετά tests καλούν τον validator απευθείας.
            Format = ProjectDocument.FormatIdentifier,
            Version = ProjectDocument.CurrentVersion,
            Name = "Δοκιμή",
            PlatformCode = "cpc",
            ModeCode = "cpc.mode0",
            PaletteProfileId = "nominal",
        };

        document.Palette.Add(new PaletteSlotDocument { Slot = 0, Color = 0 });
        document.Palette.Add(new PaletteSlotDocument { Slot = 1, Color = 26 });

        document.Groups.Add(new SpriteGroupDocument { Id = 1, Name = "Εχθροί" });

        var sprite = new SpriteDocument
        {
            Id = 1,
            GroupId = 1,
            Name = "player",
            Width = 8,
            Height = 8,
        };

        sprite.Frames.Add(new SpriteFrameDocument
        {
            Index = 0,
            Pixels = Convert.ToBase64String(new byte[64]),
        });

        document.Sprites.Add(sprite);

        var map = new SpriteMapDocument { Name = "tileset", Columns = 2, Rows = 2, CellWidth = 8, CellHeight = 8 };
        map.Cells.Add(new SpriteMapCellDocument { Column = 0, Row = 1, SpriteId = 1, FlipHorizontal = true });
        document.SpriteMaps.Add(map);

        return document;
    }

    private static ProjectDocumentReadResult RoundTrip(ProjectDocument document)
    {
        return ProjectDocumentSerializer.Read(ProjectDocumentSerializer.Write(document));
    }

    // --- Round trip ----------------------------------------------------------

    [Fact]
    public void Valid_document_round_trips_intact()
    {
        var original = ValidDocument();
        var result = RoundTrip(original);

        Assert.True(result.Success, string.Join(" ", result.Errors));

        var restored = result.Document!;

        Assert.Equal("Δοκιμή", restored.Name);
        Assert.Equal("cpc.mode0", restored.ModeCode);
        Assert.Equal(2, restored.Palette.Count);
        Assert.Equal(26, restored.Palette[1].Color);
        Assert.Single(restored.Groups);
        Assert.Single(restored.Sprites);
        Assert.Equal(1, restored.Sprites[0].GroupId);
        Assert.Single(restored.SpriteMaps);
        Assert.True(restored.SpriteMaps[0].Cells[0].FlipHorizontal);
    }

    /// <summary>
    /// Το αρχείο προορίζεται και για git — τα ελληνικά πρέπει να διαβάζονται,
    /// όχι να γίνονται δοκ...
    /// </summary>
    [Fact]
    public void Greek_text_is_written_readable_not_escaped()
    {
        var json = ProjectDocumentSerializer.WriteToString(ValidDocument());

        Assert.Contains("Δοκιμή", json, StringComparison.Ordinal);
        Assert.Contains("Εχθροί", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0394", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_uses_camel_case_property_names()
    {
        var json = ProjectDocumentSerializer.WriteToString(ValidDocument());

        Assert.Contains("\"modeCode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"spriteMaps\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ModeCode\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_and_version_are_stamped()
    {
        var restored = RoundTrip(ValidDocument()).Document!;

        Assert.Equal("retrotools-project", restored.Format);
        Assert.Equal(1, restored.Version);
    }

    // --- Απόρριψη ξένων ή χαλασμένων αρχείων --------------------------------

    [Fact]
    public void Rejects_json_that_is_not_a_retrotools_project()
    {
        var result = ProjectDocumentSerializer.Read(Encoding.UTF8.GetBytes("{\"hello\":\"world\"}"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("format", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_malformed_json_without_throwing()
    {
        var result = ProjectDocumentSerializer.Read(Encoding.UTF8.GetBytes("{ this is not json"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Μη έγκυρο JSON", StringComparison.Ordinal));
    }

    /// <summary>
    /// Άγνωστη έκδοση απορρίπτεται αντί να διαβαστεί «όσο γίνεται»: μια σιωπηλά
    /// μισοδιαβασμένη δουλειά είναι χειρότερη από ένα καθαρό σφάλμα.
    /// </summary>
    [Fact]
    public void Rejects_a_future_format_version()
    {
        // Το JSON φτιάχνεται με το χέρι: ο serializer σφραγίζει πάντα την τρέχουσα
        // έκδοση, οπότε ένα round trip δεν θα μπορούσε ποτέ να παραγάγει άλλη.
        var json = ProjectDocumentSerializer.WriteToString(ValidDocument())
            .Replace("\"version\": 1", "\"version\": 99", StringComparison.Ordinal);

        var result = ProjectDocumentSerializer.Read(Encoding.UTF8.GetBytes(json));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("99", StringComparison.Ordinal));
    }

    /// <summary>
    /// Η ταυτότητα μορφής πρέπει να υπάρχει <b>στο αρχείο</b>. Αν το μοντέλο είχε
    /// προεπιλογή, η αποσειριοποίηση θα την προσέθετε μόνη της και οποιοδήποτε JSON
    /// θα περνούσε για project του RetroTools.
    /// </summary>
    [Fact]
    public void Rejects_a_document_that_omits_the_format_field()
    {
        var json = ProjectDocumentSerializer.WriteToString(ValidDocument())
            .Replace("\"format\": \"retrotools-project\",", string.Empty, StringComparison.Ordinal);

        var result = ProjectDocumentSerializer.Read(Encoding.UTF8.GetBytes(json));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("format", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_unknown_mode()
    {
        var document = ValidDocument();
        document.ModeCode = "amiga.aga";

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("Άγνωστο mode", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_platform_that_contradicts_the_mode()
    {
        var document = ValidDocument();
        document.PlatformCode = "c64";

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("Ασυνέπεια", StringComparison.Ordinal));
    }

    // --- Έλεγχοι δεδομένων ---------------------------------------------------

    [Fact]
    public void Rejects_pixel_data_of_the_wrong_length()
    {
        var document = ValidDocument();
        document.Sprites[0].Frames[0].Pixels = Convert.ToBase64String(new byte[10]);

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("64 bytes pixels", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_colour_index_beyond_the_mode_limit()
    {
        var document = ValidDocument();
        document.ModeCode = "cpc.mode1";
        document.Palette.Clear();

        var pixels = new byte[64];
        pixels[5] = 7; // Το Mode 1 έχει μόνο pens 0–3.
        document.Sprites[0].Frames[0].Pixels = Convert.ToBase64String(pixels);

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("0–3", StringComparison.Ordinal) && e.Contains("5,0", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_sprite_dimensions_the_hardware_cannot_produce()
    {
        var document = ValidDocument();
        document.ModeCode = "c64.sprite_hires";
        document.PlatformCode = "c64";
        document.Palette.Clear();

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("24×21", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_attributes_for_a_mode_without_them()
    {
        var document = ValidDocument();
        document.Sprites[0].Frames[0].Attributes = Convert.ToBase64String(new byte[1]);

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("δεν χρησιμοποιεί attributes", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_palette_colour_outside_the_hardware_palette()
    {
        var document = ValidDocument();
        document.Palette.Add(new PaletteSlotDocument { Slot = 2, Color = 40 });

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("εκτός της παλέτας", StringComparison.Ordinal));
    }

    // --- Ακεραιότητα αναφορών ------------------------------------------------

    [Fact]
    public void Rejects_a_sprite_pointing_at_a_missing_group()
    {
        var document = ValidDocument();
        document.Sprites[0].GroupId = 99;

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("ανύπαρκτη ομάδα", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_cell_pointing_at_a_missing_sprite()
    {
        var document = ValidDocument();
        document.SpriteMaps[0].Cells[0].SpriteId = 99;

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("ανύπαρκτο sprite", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_cell_outside_its_grid()
    {
        var document = ValidDocument();
        document.SpriteMaps[0].Cells[0].Column = 5;

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("εκτός πλέγματος", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_duplicate_identifiers()
    {
        var document = ValidDocument();
        document.Sprites.Add(new SpriteDocument
        {
            Id = 1,
            Name = "δίδυμο",
            Width = 8,
            Height = 8,
            Frames = { new SpriteFrameDocument { Index = 0, Pixels = Convert.ToBase64String(new byte[64]) } },
        });

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("Διπλό αναγνωριστικό sprite", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_sprite_without_frames()
    {
        var document = ValidDocument();
        document.Sprites[0].Frames.Clear();

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("δεν έχει κανένα καρέ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Τα όρια δεν είναι διακοσμητικά: χωρίς αυτά ένα αρχείο με εκατομμύρια sprites
    /// θα κατανάλωνε τη μνήμη του διακομιστή πριν καν αποθηκευτεί τίποτα.
    /// </summary>
    [Fact]
    public void Rejects_an_absurd_number_of_sprites()
    {
        var document = ValidDocument();

        for (var i = 0; i < ProjectDocumentValidator.MaxSprites + 1; i++)
        {
            document.Sprites.Add(new SpriteDocument { Id = i + 100, Name = "s" + i, Width = 8, Height = 8 });
        }

        Assert.Contains(
            ProjectDocumentValidator.Validate(document),
            e => e.Contains("Πάρα πολλά sprites", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_every_problem_at_once_not_just_the_first()
    {
        var document = ValidDocument();
        document.Sprites[0].GroupId = 99;
        document.SpriteMaps[0].Cells[0].SpriteId = 77;
        document.Palette.Add(new PaletteSlotDocument { Slot = 3, Color = 999 });

        var errors = ProjectDocumentValidator.Validate(document);

        Assert.True(errors.Count >= 3, "Αναμένονταν τουλάχιστον τρία σφάλματα, βρέθηκαν " + errors.Count + ".");
    }
}
