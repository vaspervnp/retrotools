using RetroTools.Core.Imaging;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Tests;

public class ImageQuantizerTests
{
    /// <summary>Φτιάχνει PNG με τα δοσμένα χρώματα και το περνά από τον decoder.</summary>
    private static DecodedImage MakeImage(int width, int height, Func<int, int, Rgb24> color, int? transparentIndex = null)
    {
        var palette = new List<Rgb24>();
        var lookup = new Dictionary<Rgb24, byte>();
        var frame = new FrameBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rgb = color(x, y);

                if (!lookup.TryGetValue(rgb, out var index))
                {
                    index = (byte)palette.Count;
                    palette.Add(rgb);
                    lookup[rgb] = index;
                }

                frame[x, y] = index;
            }
        }

        return PngReader.Read(PngWriter.WriteIndexed(frame, palette, transparentIndex: transparentIndex));
    }

    private static ImageImportResult Quantize(
        DecodedImage image,
        string modeCode,
        PaletteStrategy strategy = PaletteStrategy.AutoAssign)
    {
        var mode = PlatformCatalog.GetMode(modeCode);
        var platform = PlatformCatalog.Get(mode.Platform);

        return ImageQuantizer.Quantize(image, mode, platform, new ImageImportOptions { Strategy = strategy });
    }

    // --- Ακριβή χρώματα ------------------------------------------------------

    /// <summary>
    /// Εικόνα με χρώματα που υπάρχουν αυτούσια στην παλέτα του υλικού πρέπει να
    /// περάσει χωρίς καμία απώλεια.
    /// </summary>
    [Fact]
    public void Exact_hardware_colours_survive_untouched()
    {
        var cpc = PlatformCatalog.Get("cpc");
        var black = cpc.Palette.GetRgb(0);
        var brightWhite = cpc.Palette.GetRgb(26);

        var image = MakeImage(4, 2, (x, y) => x % 2 == 0 ? black : brightWhite);
        var result = Quantize(image, "cpc.mode0");

        var profile = cpc.Palette.DefaultProfile;

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var slot = result.Frame[x, y];
                var rgb = profile[result.SlotColors[slot]];

                Assert.Equal(x % 2 == 0 ? black : brightWhite, rgb);
            }
        }

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Auto_assign_picks_the_colours_the_image_actually_uses()
    {
        var cpc = PlatformCatalog.Get("cpc");
        var orange = cpc.Palette.GetRgb(15);
        var seaGreen = cpc.Palette.GetRgb(19);

        // Το πορτοκαλί καλύπτει τα τρία τέταρτα, άρα πρέπει να πάρει το πρώτο slot.
        var image = MakeImage(4, 1, (x, y) => x < 3 ? orange : seaGreen);
        var result = Quantize(image, "cpc.mode0");

        Assert.Equal(15, result.SlotColors[0]);
        Assert.Equal(19, result.SlotColors[1]);
        Assert.Equal(0, result.Frame[0, 0]);
        Assert.Equal(1, result.Frame[3, 0]);
    }

    /// <summary>
    /// Κοντινές αποχρώσεις που στρογγυλοποιούνται στο ίδιο χρώμα υλικού δεν πρέπει
    /// να σπαταλούν δύο slots — γι' αυτό η ομαδοποίηση γίνεται μετά τη στρογγυλοποίηση.
    /// </summary>
    [Fact]
    public void Near_identical_shades_do_not_waste_two_slots()
    {
        var image = MakeImage(4, 1, (x, y) => x < 2
            ? new Rgb24(0xFE, 0xFE, 0xFE)
            : new Rgb24(0xFD, 0xFD, 0xFD));

        var result = Quantize(image, "cpc.mode0");

        // Και τα δύο καταλήγουν στο ίδιο slot.
        Assert.Equal(result.Frame[0, 0], result.Frame[3, 0]);
    }

    // --- Διαφάνεια -----------------------------------------------------------

    /// <summary>
    /// Ένα διαφανές pixel πρέπει να πάει στο slot διαφάνειας του mode, όχι στο
    /// πλησιέστερο χρώμα — αλλιώς ένα C64 sprite θα γέμιζε φόντο.
    /// </summary>
    [Fact]
    public void Transparent_pixels_map_to_the_transparent_slot()
    {
        var image = MakeImage(
            4,
            1,
            (x, y) => x < 2 ? new Rgb24(0, 0, 0) : new Rgb24(255, 255, 255),
            transparentIndex: 0);

        var result = Quantize(image, "c64.sprite_multicolor");

        Assert.Equal(0, result.Frame[0, 0]);
        Assert.Equal(0, result.Frame[1, 0]);
        Assert.NotEqual(0, result.Frame[2, 0]);
    }

    /// <summary>
    /// Και το αντίστροφο: ένα <b>αδιαφανές</b> pixel δεν επιτρέπεται να καταλήξει
    /// στο slot διαφάνειας επειδή έτυχε να μοιάζει με ό,τι έχει ανατεθεί εκεί.
    /// </summary>
    [Fact]
    public void Opaque_pixels_never_land_in_the_transparent_slot()
    {
        var image = MakeImage(8, 1, (x, y) => new Rgb24(0, 0, 0));
        var result = Quantize(image, "c64.sprite_hires");

        for (var x = 0; x < 8; x++)
        {
            Assert.NotEqual(0, result.Frame[x, 0]);
        }
    }

    // --- Προειδοποιήσεις -----------------------------------------------------

    [Fact]
    public void Warns_when_the_image_has_more_colours_than_the_mode_allows()
    {
        var cpc = PlatformCatalog.Get("cpc");

        // Οκτώ διαφορετικά χρώματα υλικού σε mode με τέσσερα pens.
        var image = MakeImage(8, 1, (x, y) => cpc.Palette.GetRgb(x * 3));
        var result = Quantize(image, "cpc.mode1");

        Assert.Contains(result.Warnings, w => w.Contains("slots", StringComparison.Ordinal));
    }

    /// <summary>
    /// Μια φωτογραφική εικόνα σε ZX Spectrum σχεδόν σίγουρα παραβιάζει το όριο των
    /// δύο χρωμάτων ανά κελί. Ο χρήστης πρέπει να το μάθει τώρα, όχι στον emulator.
    /// </summary>
    [Fact]
    public void Warns_about_attribute_clash_on_the_spectrum()
    {
        var zx = PlatformCatalog.Get("zx");

        // Τέσσερα χρώματα μέσα σε ένα κελί 8×8.
        var image = MakeImage(8, 8, (x, y) => zx.Palette.GetRgb((x / 2) % 4 * 4));
        var result = Quantize(image, "zx.sprite");

        Assert.Contains(result.Warnings, w => w.Contains("attribute clash", StringComparison.Ordinal));
    }

    [Fact]
    public void Quiet_image_produces_no_warnings()
    {
        var zx = PlatformCatalog.Get("zx");
        var image = MakeImage(8, 8, (x, y) => zx.Palette.GetRgb(x < 4 ? 0 : 15));

        Assert.Empty(Quantize(image, "zx.sprite").Warnings);
    }

    // --- Στρατηγική παλέτας --------------------------------------------------

    /// <summary>
    /// Με τη στρατηγική «κράτα την παλέτα του project» τα slots δεν αλλάζουν —
    /// η εικόνα προσαρμόζεται σε αυτά, όχι το αντίστροφο.
    /// </summary>
    [Fact]
    public void Project_palette_strategy_leaves_the_slots_alone()
    {
        var mode = PlatformCatalog.GetMode("cpc.mode1");
        var platform = PlatformCatalog.Get("cpc");
        var existing = new[] { 0, 6, 18, 2 };

        var image = MakeImage(4, 1, (x, y) => platform.Palette.GetRgb(24));

        var result = ImageQuantizer.Quantize(image, mode, platform, new ImageImportOptions
        {
            Strategy = PaletteStrategy.UseProjectPalette,
            ProjectSlotColors = existing,
        });

        Assert.Equal(existing, result.SlotColors);

        // Το bright yellow δεν υπάρχει στην παλέτα· ταιριάζεται στο πλησιέστερο.
        Assert.InRange(result.Frame[0, 0], (byte)0, (byte)3);
    }

    [Fact]
    public void Result_never_exceeds_the_mode_pixel_limit()
    {
        var cpc = PlatformCatalog.Get("cpc");

        foreach (var modeCode in new[] { "cpc.mode0", "cpc.mode1", "cpc.mode2", "zx.sprite", "c64.sprite_multicolor" })
        {
            var mode = PlatformCatalog.GetMode(modeCode);
            var image = MakeImage(8, 8, (x, y) => cpc.Palette.GetRgb((x + y) % 27));
            var result = Quantize(image, modeCode);

            Assert.True(
                result.Frame.MaxValue <= mode.MaxPixelValue,
                modeCode + ": τιμή " + result.Frame.MaxValue + " πάνω από το όριο " + mode.MaxPixelValue + ".");
        }
    }

    [Fact]
    public void Dimensions_come_from_the_image()
    {
        var image = MakeImage(12, 5, (x, y) => new Rgb24(0, 0, 0));
        var result = Quantize(image, "cpc.mode0");

        Assert.Equal(12, result.Frame.Width);
        Assert.Equal(5, result.Frame.Height);
    }
}
