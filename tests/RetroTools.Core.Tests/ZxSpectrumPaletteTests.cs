using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class ZxSpectrumPaletteTests
{
    private static readonly HardwarePalette Palette = ZxSpectrumPlatform.CreatePalette();

    /// <summary>
    /// 16 δείκτες αλλά 15 χρώματα: το "bright black" είναι το ίδιο μαύρο.
    /// Αυτή η λεπτομέρεια πρέπει να φαίνεται στον χρήστη, γι' αυτό την ελέγχουμε.
    /// </summary>
    [Fact]
    public void Sixteen_indices_produce_fifteen_distinct_colors()
    {
        Assert.Equal(16, Palette.Count);
        Assert.Equal(15, Palette.CountDistinctColors());
        Assert.Equal(Palette.GetRgb(0), Palette.GetRgb(8));
    }

    /// <summary>Τα bits του χρώματος είναι σε σειρά GRB: bit0 = Blue, bit1 = Red, bit2 = Green.</summary>
    [Theory]
    [InlineData(0, "Black", "#000000")]
    [InlineData(1, "Blue", "#0000D8")]
    [InlineData(2, "Red", "#D80000")]
    [InlineData(3, "Magenta", "#D800D8")]
    [InlineData(4, "Green", "#00D800")]
    [InlineData(5, "Cyan", "#00D8D8")]
    [InlineData(6, "Yellow", "#D8D800")]
    [InlineData(7, "White", "#D8D8D8")]
    public void Normal_colors_use_the_grb_bit_order(int index, string name, string hex)
    {
        Assert.Equal(name, Palette[index].Name);
        Assert.Equal(hex, Palette.GetRgb(index).ToHex());
    }

    [Theory]
    [InlineData(8, "Bright Black", "#000000")]
    [InlineData(9, "Bright Blue", "#0000FF")]
    [InlineData(10, "Bright Red", "#FF0000")]
    [InlineData(14, "Bright Yellow", "#FFFF00")]
    [InlineData(15, "Bright White", "#FFFFFF")]
    public void Bright_colors_use_full_intensity(int index, string name, string hex)
    {
        Assert.Equal(name, Palette[index].Name);
        Assert.Equal(hex, Palette.GetRgb(index).ToHex());
    }

    [Fact]
    public void D7_profile_differs_only_in_the_non_bright_level()
    {
        var d7 = Palette.GetProfile("d7");

        Assert.Equal("#0000D7", d7[1].ToHex());
        Assert.Equal("#0000FF", d7[9].ToHex());
        Assert.Equal("#000000", d7[0].ToHex());
    }

    // --- Attribute byte -----------------------------------------------------

    [Theory]
    [InlineData(0, 0, false, false, 0x00)]
    [InlineData(7, 0, false, false, 0x07)]
    [InlineData(0, 7, false, false, 0x38)]
    [InlineData(0, 0, true, false, 0x40)]
    [InlineData(0, 0, false, true, 0x80)]
    [InlineData(2, 5, true, true, 0xEA)]
    public void Attribute_byte_layout_matches_hardware(int ink, int paper, bool bright, bool flash, int expected)
    {
        Assert.Equal((byte)expected, ZxSpectrumPlatform.MakeAttribute(ink, paper, bright, flash));
    }

    [Fact]
    public void Attribute_round_trips()
    {
        for (var ink = 0; ink < 8; ink++)
        {
            for (var paper = 0; paper < 8; paper++)
            {
                var attribute = ZxSpectrumPlatform.MakeAttribute(ink, paper, bright: true, flash: false);
                var parsed = ZxSpectrumPlatform.ReadAttribute(attribute);

                Assert.Equal(ink, parsed.Ink);
                Assert.Equal(paper, parsed.Paper);
                Assert.True(parsed.Bright);
                Assert.False(parsed.Flash);
            }
        }
    }

    [Fact]
    public void Attribute_rejects_out_of_range_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZxSpectrumPlatform.MakeAttribute(8, 0, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZxSpectrumPlatform.MakeAttribute(0, -1, false, false));
    }

    // --- Διάταξη μνήμης -----------------------------------------------------

    /// <summary>
    /// Η μη γραμμική διάταξη της οθόνης του Spectrum είναι η πιο συχνή πηγή
    /// λαθών σε export. Ελέγχουμε τα άκρα και τα όρια των τριτημορίων.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0x4000)]     // πρώτο byte
    [InlineData(1, 0, 0x4100)]     // δεύτερη scanline του ίδιου χαρακτήρα
    [InlineData(8, 0, 0x4020)]     // δεύτερη σειρά χαρακτήρων
    [InlineData(64, 0, 0x4800)]    // αρχή του δεύτερου τριτημορίου
    [InlineData(128, 0, 0x5000)]   // αρχή του τρίτου τριτημορίου
    [InlineData(191, 31, 0x57FF)]  // τελευταίο byte του bitmap
    public void Bitmap_address_follows_the_thirds_layout(int y, int column, int expected)
    {
        Assert.Equal((ushort)expected, ZxSpectrumPlatform.GetBitmapAddress(y, column));
    }

    [Fact]
    public void Bitmap_covers_exactly_6144_unique_addresses()
    {
        var addresses = new HashSet<ushort>();

        for (var y = 0; y < ZxSpectrumPlatform.ScreenHeight; y++)
        {
            for (var column = 0; column < ZxSpectrumPlatform.AttributeColumns; column++)
            {
                addresses.Add(ZxSpectrumPlatform.GetBitmapAddress(y, column));
            }
        }

        Assert.Equal(6144, addresses.Count);
        Assert.Equal(0x4000, addresses.Min());
        Assert.Equal(0x57FF, addresses.Max());
    }

    [Theory]
    [InlineData(0, 0, 0x5800)]
    [InlineData(7, 0, 0x5800)]     // και οι 8 scanlines μοιράζονται ένα attribute
    [InlineData(8, 0, 0x5820)]
    [InlineData(191, 31, 0x5AFF)]  // τελευταίο attribute byte
    public void Attribute_address_is_linear(int y, int column, int expected)
    {
        Assert.Equal((ushort)expected, ZxSpectrumPlatform.GetAttributeAddress(y, column));
    }

    [Fact]
    public void Attribute_grid_is_32_by_24()
    {
        Assert.Equal(32, ZxSpectrumPlatform.AttributeColumns);
        Assert.Equal(24, ZxSpectrumPlatform.AttributeRows);
        Assert.Equal(768, ZxSpectrumPlatform.AttributeColumns * ZxSpectrumPlatform.AttributeRows);
    }

    // --- Modes --------------------------------------------------------------

    [Fact]
    public void Sprite_mode_enforces_byte_alignment_and_two_colors_per_cell()
    {
        var mode = PlatformCatalog.GetMode("zx.sprite");

        Assert.Equal(ColorScope.PerCell, mode.ColorScope);
        Assert.Equal(2, mode.MaxColorsPerCell);
        Assert.Equal(8, mode.CellWidth);
        Assert.Equal(8, mode.CellHeight);
        Assert.Equal(8, mode.SpriteSize.WidthAlignment);

        Assert.True(mode.SpriteSize.IsValid(16, 21));
        Assert.False(mode.SpriteSize.IsValid(12, 16));
    }

    [Fact]
    public void Udg_is_fixed_at_8x8_and_takes_8_bytes()
    {
        var mode = PlatformCatalog.GetMode("zx.udg");

        Assert.True(mode.SpriteSize.IsFixed);
        Assert.Equal(8, mode.PackedSize(8, 8));
    }
}
