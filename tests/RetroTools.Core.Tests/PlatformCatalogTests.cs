using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Tests;

public class PlatformCatalogTests
{
    [Fact]
    public void Catalog_contains_the_three_supported_platforms()
    {
        var codes = PlatformCatalog.All.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "c64", "cpc", "zx" }, codes);
    }

    [Fact]
    public void Platform_codes_and_mode_codes_are_unique()
    {
        var codes = PlatformCatalog.All.Select(p => p.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var modeCodes = PlatformCatalog.All.SelectMany(p => p.Modes).Select(m => m.Code).ToList();
        Assert.Equal(modeCodes.Count, modeCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Mode_codes_are_prefixed_with_their_platform_code()
    {
        foreach (var platform in PlatformCatalog.All)
        {
            foreach (var mode in platform.Modes)
            {
                Assert.StartsWith(platform.Code + ".", mode.Code, StringComparison.Ordinal);
                Assert.Equal(platform.Id, mode.Platform);
            }
        }
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        Assert.Equal("cpc", PlatformCatalog.Get("CPC").Code);
        Assert.Equal("cpc.mode0", PlatformCatalog.GetMode("CPC.MODE0").Code);
    }

    [Fact]
    public void Unknown_lookup_lists_the_available_options()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => PlatformCatalog.Get("amiga"));

        Assert.Contains("cpc", exception.Message, StringComparison.Ordinal);
        Assert.Contains("c64", exception.Message, StringComparison.Ordinal);
        Assert.Contains("zx", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Οι δείκτες pixel πρέπει να χωράνε στα bits που δίνει το mode.
    /// Σε per-pixel/per-sprite modes ο δείκτης δείχνει στην παλέτα·
    /// σε per-cell modes δείχνει στα χρώματα του κελιού.
    /// </summary>
    [Fact]
    public void Palette_slots_fit_in_the_available_bits_per_pixel()
    {
        foreach (var mode in PlatformCatalog.AllModes)
        {
            if (mode.ColorScope == ColorScope.PerCell)
            {
                Assert.Equal(mode.PixelValueCount, mode.MaxColorsPerCell);
            }
            else
            {
                Assert.True(
                    mode.PaletteSlots <= mode.PixelValueCount,
                    mode.Code + ": " + mode.PaletteSlots + " slots δεν χωράνε σε " + mode.BitsPerPixel + " bits.");
            }

            Assert.InRange(mode.BitsPerPixel, 1, 8);
            Assert.Equal(8 / mode.BitsPerPixel, mode.PixelsPerByte);
        }
    }

    [Fact]
    public void Every_mode_has_a_usable_sprite_size()
    {
        foreach (var mode in PlatformCatalog.AllModes)
        {
            var rule = mode.SpriteSize;
            var width = rule.FixedWidth ?? rule.WidthAlignment * 2;
            var height = rule.FixedHeight ?? 16;

            Assert.True(rule.IsValid(width, height), mode.Code + ": " + width + "×" + height + " απορρίφθηκε.");
            Assert.True(mode.PackedSize(width, height) > 0);
        }
    }

    /// <summary>
    /// Κάθε δυνατή τιμή pixel πρέπει να έχει ορισμένη σημασία, αλλιώς ο editor
    /// θα δείχνει slots που δεν αντιστοιχούν σε τίποτα.
    /// </summary>
    [Fact]
    public void Pixel_slots_cover_every_possible_pixel_value_exactly_once()
    {
        foreach (var mode in PlatformCatalog.AllModes)
        {
            var slots = mode.PixelSlots;

            Assert.Equal(mode.MaxPixelValue + 1, slots.Count);
            Assert.Equal(Enumerable.Range(0, slots.Count), slots.Select(s => s.Index));
            Assert.All(slots, s => Assert.False(string.IsNullOrWhiteSpace(s.Name), mode.Code + ": slot χωρίς όνομα."));
        }
    }

    [Fact]
    public void Only_c64_has_shared_colour_registers()
    {
        var platformsWithSharedSlots = PlatformCatalog.All
            .Where(p => p.Modes.Any(m => m.SharedSlots.Count > 0))
            .Select(p => p.Code)
            .ToList();

        Assert.Equal(new[] { "c64" }, platformsWithSharedSlots);
    }

    [Fact]
    public void Cpc_modes_use_freely_assignable_pens()
    {
        foreach (var mode in PlatformCatalog.Get("cpc").Modes)
        {
            Assert.All(mode.PixelSlots, s => Assert.Equal(PixelSlotRole.Free, s.Role));
        }
    }

    [Fact]
    public void Per_cell_modes_declare_their_cell_dimensions()
    {
        foreach (var mode in PlatformCatalog.AllModes.Where(m => m.ColorScope == ColorScope.PerCell))
        {
            Assert.True(mode.CellWidth > 0, mode.Code + ": λείπει το CellWidth.");
            Assert.True(mode.CellHeight > 0, mode.Code + ": λείπει το CellHeight.");
        }
    }

    [Fact]
    public void Only_cpc_has_a_programmable_palette()
    {
        var programmable = PlatformCatalog.All.Where(p => p.HasProgrammablePalette).Select(p => p.Code).ToList();

        Assert.Equal(new[] { "cpc" }, programmable);
    }

    [Fact]
    public void Cpu_family_drives_the_assembler_exporter()
    {
        Assert.Equal(CpuFamily.Z80, PlatformCatalog.Get("cpc").Cpu);
        Assert.Equal(CpuFamily.Z80, PlatformCatalog.Get("zx").Cpu);
        Assert.Equal(CpuFamily.Mos6502, PlatformCatalog.Get("c64").Cpu);
    }

    [Fact]
    public void Every_palette_has_a_default_profile_with_matching_colour_count()
    {
        foreach (var platform in PlatformCatalog.All)
        {
            var palette = platform.Palette;

            Assert.NotEmpty(palette.Profiles);
            Assert.Contains(palette.DefaultProfile, palette.Profiles);

            foreach (var profile in palette.Profiles)
            {
                Assert.Equal(palette.Count, profile.Colors.Count);
            }
        }
    }

    [Fact]
    public void Nearest_colour_finds_exact_matches()
    {
        foreach (var platform in PlatformCatalog.All)
        {
            var palette = platform.Palette;

            for (var i = 0; i < palette.Count; i++)
            {
                var rgb = palette.GetRgb(i);
                var found = palette.FindNearest(rgb);

                // Σε πλατφόρμες με διπλότυπα χρώματα (ZX bright black) αρκεί να
                // βρεθεί κάποιο χρώμα με τα ίδια ακριβώς RGB.
                Assert.Equal(rgb, palette.GetRgb(found));
            }
        }
    }
}

public class Rgb24Tests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("6C5EB5", 0x6C, 0x5E, 0xB5)]
    public void Parses_hex_with_or_without_hash(string hex, int r, int g, int b)
    {
        var color = Rgb24.FromHex(hex);

        Assert.Equal((byte)r, color.R);
        Assert.Equal((byte)g, color.G);
        Assert.Equal((byte)b, color.B);
    }

    [Fact]
    public void Round_trips_through_hex()
    {
        var color = new Rgb24(0x12, 0xAB, 0xCD);

        Assert.Equal("#12ABCD", color.ToHex());
        Assert.Equal(color, Rgb24.FromHex(color.ToHex()));
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#1234567")]
    [InlineData("")]
    public void Rejects_malformed_hex(string hex)
    {
        Assert.Throws<FormatException>(() => Rgb24.FromHex(hex));
    }

    [Fact]
    public void Distance_to_itself_is_zero_and_black_to_white_is_the_maximum()
    {
        var black = new Rgb24(0, 0, 0);
        var white = new Rgb24(255, 255, 255);
        var grey = new Rgb24(128, 128, 128);

        Assert.Equal(0, black.LinearDistanceSquaredTo(black));
        Assert.True(black.LinearDistanceSquaredTo(grey) < black.LinearDistanceSquaredTo(white));
    }
}
