using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class AmstradCpcPaletteTests
{
    private static readonly HardwarePalette Palette = AmstradCpcPlatform.CreatePalette();

    [Fact]
    public void Palette_has_27_colors()
    {
        Assert.Equal(27, Palette.Count);
        Assert.Equal(27, Palette.CountDistinctColors());
    }

    [Fact]
    public void Indices_are_sequential_and_unique()
    {
        var indices = Palette.Colors.Select(c => c.Index).ToList();
        Assert.Equal(Enumerable.Range(0, 27), indices);
    }

    /// <summary>
    /// Το firmware colour number δεν είναι αυθαίρετο: κωδικοποιεί τα επίπεδα RGB
    /// ως index = 3·R + 9·G + B. Αν σπάσει αυτό, ο πίνακας χρωμάτων έχει λάθος.
    /// </summary>
    [Fact]
    public void Firmware_index_encodes_rgb_levels_in_base_3()
    {
        for (var index = 0; index < AmstradCpcPlatform.ColorCount; index++)
        {
            var levels = AmstradCpcPlatform.GetLevels(index);

            Assert.InRange(levels.R, 0, 2);
            Assert.InRange(levels.G, 0, 2);
            Assert.InRange(levels.B, 0, 2);
            Assert.Equal(index, (3 * levels.R) + (9 * levels.G) + levels.B);
        }
    }

    [Fact]
    public void All_27_level_combinations_are_present_exactly_once()
    {
        var combinations = Enumerable.Range(0, AmstradCpcPlatform.ColorCount)
            .Select(AmstradCpcPlatform.GetLevels)
            .ToList();

        Assert.Equal(27, combinations.Distinct().Count());
    }

    /// <summary>
    /// Ο Gate Array δέχεται 32 τιμές ink (0x40–0x5F) για 27 χρώματα.
    /// Κάθε τιμή πρέπει να χρησιμοποιείται ακριβώς μία φορά.
    /// </summary>
    [Fact]
    public void Gate_array_inks_cover_0x40_to_0x5F_exactly_once()
    {
        var allInks = Palette.Colors.SelectMany(c => c.HardwareValues).ToList();

        Assert.Equal(32, allInks.Count);
        Assert.Equal(32, allInks.Distinct().Count());
        Assert.All(allInks, ink => Assert.InRange(ink, (byte)0x40, (byte)0x5F));
    }

    [Fact]
    public void Exactly_five_colors_have_a_duplicate_hardware_ink()
    {
        var duplicated = Palette.Colors
            .Where(c => c.HardwareValues.Count == 2)
            .Select(c => c.Index)
            .OrderBy(i => i)
            .ToList();

        Assert.Equal(new[] { 1, 7, 13, 19, 25 }, duplicated);
        Assert.All(Palette.Colors, c => Assert.InRange(c.HardwareValues.Count, 1, 2));
    }

    [Theory]
    [InlineData(0x54, 0, "Black")]
    [InlineData(0x4B, 26, "Bright White")]
    [InlineData(0x40, 13, "White")]
    [InlineData(0x41, 13, "White")]
    [InlineData(0x5F, 14, "Pastel Blue")]
    [InlineData(0x4E, 15, "Orange")]
    [InlineData(0x52, 18, "Bright Green")]
    [InlineData(0x57, 11, "Sky Blue")]
    [InlineData(0x59, 22, "Pastel Green")]
    [InlineData(0x43, 25, "Pastel Yellow")]
    public void Hardware_ink_maps_to_expected_firmware_color(int ink, int expectedIndex, string expectedName)
    {
        Assert.True(Palette.TryGetIndexByHardwareValue((byte)ink, out var index));
        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedName, Palette[index].Name);
    }

    [Theory]
    [InlineData(0, "#000000")]
    [InlineData(1, "#000080")]
    [InlineData(2, "#0000FF")]
    [InlineData(6, "#FF0000")]
    [InlineData(11, "#0080FF")]
    [InlineData(13, "#808080")]
    [InlineData(15, "#FF8000")]
    [InlineData(20, "#00FFFF")]
    [InlineData(24, "#FFFF00")]
    [InlineData(26, "#FFFFFF")]
    public void Nominal_profile_produces_expected_rgb(int index, string expectedHex)
    {
        Assert.Equal(expectedHex, Palette.GetRgb(index).ToHex());
    }

    [Fact]
    public void Measured_profile_only_changes_the_mid_level()
    {
        var measured = Palette.GetProfile("measured");

        // Τα άκρα (0% και 100%) είναι ίδια και στα δύο profiles.
        Assert.Equal("#000000", measured[0].ToHex());
        Assert.Equal("#FFFFFF", measured[26].ToHex());

        // Το μεσαίο επίπεδο είναι σκουρότερο από το ονομαστικό 0x80.
        Assert.True(measured[13].R < 0x80);
        Assert.Equal(measured[13].R, measured[13].G);
        Assert.Equal(measured[13].R, measured[13].B);
    }

    [Fact]
    public void Unknown_profile_falls_back_to_default_instead_of_throwing()
    {
        Assert.Equal(Palette.DefaultProfile, Palette.GetProfile("δεν-υπάρχει"));
        Assert.Equal("nominal", Palette.DefaultProfile.Id);
    }

    [Fact]
    public void Modes_have_the_documented_byte_alignment()
    {
        var cpc = PlatformCatalog.Get(AmstradCpcPlatform.Code);

        Assert.Equal(2, cpc.GetMode("cpc.mode0").SpriteSize.WidthAlignment);
        Assert.Equal(4, cpc.GetMode("cpc.mode1").SpriteSize.WidthAlignment);
        Assert.Equal(8, cpc.GetMode("cpc.mode2").SpriteSize.WidthAlignment);
    }

    [Theory]
    [InlineData("cpc.mode0", 16, 4, 2)]
    [InlineData("cpc.mode1", 4, 2, 4)]
    [InlineData("cpc.mode2", 2, 1, 8)]
    public void Mode_colour_counts_match_bits_per_pixel(string modeCode, int colors, int bpp, int pixelsPerByte)
    {
        var mode = PlatformCatalog.GetMode(modeCode);

        Assert.Equal(colors, mode.PaletteSlots);
        Assert.Equal(bpp, mode.BitsPerPixel);
        Assert.Equal(pixelsPerByte, mode.PixelsPerByte);
    }

    [Fact]
    public void Mode0_sprite_of_16x16_needs_128_bytes()
    {
        var mode0 = PlatformCatalog.GetMode("cpc.mode0");

        // 16 pixels / 2 ανά byte = 8 bytes ανά γραμμή, × 16 γραμμές.
        Assert.Equal(8, mode0.BytesPerRow(16));
        Assert.Equal(128, mode0.PackedSize(16, 16));
    }

    [Fact]
    public void Odd_width_is_rejected_in_mode0()
    {
        var rule = PlatformCatalog.GetMode("cpc.mode0").SpriteSize;

        Assert.True(rule.IsValid(16, 16));
        Assert.False(rule.IsValid(15, 16));
        Assert.Contains(rule.Validate(15, 16), e => e.Contains("πολλαπλάσιο του 2", StringComparison.Ordinal));
    }
}
