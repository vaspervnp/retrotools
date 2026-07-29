using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class Commodore64PaletteTests
{
    private static readonly HardwarePalette Palette = Commodore64Platform.CreatePalette();

    [Fact]
    public void Palette_has_16_distinct_colors()
    {
        Assert.Equal(16, Palette.Count);
        Assert.Equal(16, Palette.CountDistinctColors());
    }

    /// <summary>Οι τιμές της παλέτας Pepto, από pepto.de/projects/colorvic.</summary>
    [Theory]
    [InlineData(0, "Black", "#000000")]
    [InlineData(1, "White", "#FFFFFF")]
    [InlineData(2, "Red", "#68372B")]
    [InlineData(3, "Cyan", "#70A4B2")]
    [InlineData(4, "Purple", "#6F3D86")]
    [InlineData(5, "Green", "#588D43")]
    [InlineData(6, "Blue", "#352879")]
    [InlineData(7, "Yellow", "#B8C76F")]
    [InlineData(8, "Orange", "#6F4F25")]
    [InlineData(9, "Brown", "#433900")]
    [InlineData(10, "Light Red", "#9A6759")]
    [InlineData(11, "Dark Grey", "#444444")]
    [InlineData(12, "Grey", "#6C6C6C")]
    [InlineData(13, "Light Green", "#9AD284")]
    [InlineData(14, "Light Blue", "#6C5EB5")]
    [InlineData(15, "Light Grey", "#959595")]
    public void Pepto_palette_matches_reference(int index, string name, string hex)
    {
        Assert.Equal(name, Palette[index].Name);
        Assert.Equal(hex, Palette.GetRgb(index).ToHex());
    }

    [Fact]
    public void Color_index_is_the_hardware_value_because_palette_is_not_programmable()
    {
        Assert.False(PlatformCatalog.Get(Commodore64Platform.Code).HasProgrammablePalette);

        foreach (var color in Palette.Colors)
        {
            Assert.Equal((byte)color.Index, color.PrimaryHardwareValue);
        }
    }

    [Fact]
    public void C64_is_the_only_platform_with_hardware_sprites()
    {
        var withHardwareSprites = PlatformCatalog.All.Where(p => p.HasHardwareSprites).ToList();

        Assert.Single(withHardwareSprites);
        Assert.Equal(PlatformId.Commodore64, withHardwareSprites[0].Id);
    }

    /// <summary>
    /// Και τα δύο hardware sprite modes καταλήγουν σε 63 bytes — αυτό είναι
    /// το μέγεθος που περιμένει το VIC-II, ανεξάρτητα από hi-res ή multicolor.
    /// </summary>
    [Theory]
    [InlineData("c64.sprite_hires", 24, 21)]
    [InlineData("c64.sprite_multicolor", 12, 21)]
    public void Hardware_sprites_are_63_bytes(string modeCode, int width, int height)
    {
        var mode = PlatformCatalog.GetMode(modeCode);

        Assert.True(mode.IsHardwareSprite);
        Assert.True(mode.SpriteSize.IsFixed);
        Assert.Equal(width, mode.SpriteSize.FixedWidth);
        Assert.Equal(height, mode.SpriteSize.FixedHeight);
        Assert.Equal(3, mode.BytesPerRow(width));
        Assert.Equal(Commodore64Platform.SpriteDataSize, mode.PackedSize(width, height));
    }

    [Fact]
    public void Hardware_sprite_size_cannot_be_changed()
    {
        var rule = PlatformCatalog.GetMode("c64.sprite_hires").SpriteSize;

        Assert.True(rule.IsValid(24, 21));
        Assert.False(rule.IsValid(24, 22));
        Assert.False(rule.IsValid(16, 16));
        Assert.Contains(rule.Validate(16, 16), e => e.Contains("24×21", StringComparison.Ordinal));
    }

    [Fact]
    public void Multicolor_sprite_has_four_slots_and_wide_pixels()
    {
        var mode = PlatformCatalog.GetMode("c64.sprite_multicolor");

        Assert.Equal(4, mode.PaletteSlots);
        Assert.Equal(2, mode.BitsPerPixel);
        Assert.Equal(PixelAspect.Wide, mode.PixelAspect);
    }

    /// <summary>
    /// Ο σημαντικότερος περιορισμός του C64 multicolor sprite: δύο από τα τέσσερα
    /// χρώματα είναι <b>κοινοί καταχωρητές</b> ($D025/$D026). Αν ο editor δεν το ξέρει,
    /// ο χρήστης θα ζωγραφίσει sprites που δεν μπορούν να συνυπάρξουν στην οθόνη.
    /// </summary>
    [Fact]
    public void Multicolor_sprite_slots_1_and_3_are_shared_across_all_sprites()
    {
        var mode = PlatformCatalog.GetMode("c64.sprite_multicolor");

        Assert.Equal(PixelSlotRole.Transparent, mode.PixelSlots[0].Role);
        Assert.Equal(PixelSlotRole.Shared, mode.PixelSlots[1].Role);
        Assert.Equal(PixelSlotRole.PerObject, mode.PixelSlots[2].Role);
        Assert.Equal(PixelSlotRole.Shared, mode.PixelSlots[3].Role);

        Assert.Equal("$D025", mode.PixelSlots[1].HardwareRegister);
        Assert.Equal("$D026", mode.PixelSlots[3].HardwareRegister);
        Assert.Equal("$D027+n", mode.PixelSlots[2].HardwareRegister);

        Assert.Equal(2, mode.SharedSlots.Count);
    }

    [Fact]
    public void Hires_sprite_has_transparency_and_one_free_colour()
    {
        var mode = PlatformCatalog.GetMode("c64.sprite_hires");

        Assert.True(mode.HasTransparentSlot);
        Assert.Empty(mode.SharedSlots);
        Assert.Equal(PixelSlotRole.PerObject, mode.PixelSlots[1].Role);
    }

    [Fact]
    public void Multicolor_char_has_only_one_per_character_colour()
    {
        var mode = PlatformCatalog.GetMode("c64.char_multicolor");

        Assert.Equal(3, mode.SharedSlots.Count);
        Assert.Single(mode.PixelSlots, s => s.Role == PixelSlotRole.PerObject);
    }

    [Fact]
    public void Sprite_block_is_64_bytes_with_one_unused()
    {
        Assert.Equal(64, Commodore64Platform.SpriteBlockSize);
        Assert.Equal(63, Commodore64Platform.SpriteDataSize);
        Assert.Equal(8, Commodore64Platform.HardwareSpriteCount);
    }
}
