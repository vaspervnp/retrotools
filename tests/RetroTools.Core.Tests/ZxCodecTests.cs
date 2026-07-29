using RetroTools.Core.Codecs;
using RetroTools.Core.Model;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class ZxCodecTests
{
    // --- Bitmap --------------------------------------------------------------

    [Fact]
    public void Sprite_bitmap_is_one_bit_per_pixel_msb_first()
    {
        var codec = SpriteCodecs.For("zx.sprite");
        var frame = new FrameBuffer(8, 1);
        frame[0, 0] = 1;
        frame[7, 0] = 1;

        Assert.Equal(new byte[] { 0x81 }, codec.Pack(frame));
    }

    [Fact]
    public void Sixteen_pixel_wide_sprite_uses_two_bytes_per_row()
    {
        var codec = SpriteCodecs.For("zx.sprite");

        Assert.Equal(2, codec.BytesPerRow(16));
        Assert.Equal(32, codec.GetPackedSize(16, 16));
    }

    [Fact]
    public void Udg_is_eight_bytes()
    {
        var codec = SpriteCodecs.For("zx.udg");
        var frame = new FrameBuffer(8, 8);

        for (var i = 0; i < 8; i++)
        {
            frame[i, i] = 1;
        }

        var packed = codec.Pack(frame);

        Assert.Equal(8, packed.Length);
        Assert.Equal(new byte[] { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 }, packed);
    }

    [Fact]
    public void Pack_then_unpack_returns_the_original()
    {
        var codec = SpriteCodecs.For("zx.sprite");
        var frame = new FrameBuffer(24, 21);

        for (var y = 0; y < 21; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                frame[x, y] = (byte)((x + y) % 2);
            }
        }

        Assert.True(frame.HasSamePixels(codec.Unpack(codec.Pack(frame), 24, 21)));
    }

    // --- Attributes ----------------------------------------------------------

    [Fact]
    public void Attribute_grid_covers_the_sprite_in_8x8_cells()
    {
        var grid = AttributeGrid.ForSprite(24, 21);

        // 21 γραμμές χρειάζονται 3 σειρές κελιών (η τελευταία μισογεμάτη).
        Assert.Equal(3, grid.Columns);
        Assert.Equal(3, grid.Rows);
    }

    [Fact]
    public void Attribute_grid_defaults_to_white_ink_on_black_paper()
    {
        var grid = AttributeGrid.ForSprite(16, 16);
        var cell = grid.ReadCell(0, 0);

        Assert.Equal(7, cell.Ink);
        Assert.Equal(0, cell.Paper);
        Assert.False(cell.Bright);
        Assert.False(cell.Flash);
    }

    [Fact]
    public void Attribute_grid_round_trips_through_bytes()
    {
        var grid = AttributeGrid.ForSprite(16, 16);
        grid.SetCell(0, 0, ink: 2, paper: 5, bright: true, flash: false);
        grid.SetCell(1, 1, ink: 6, paper: 1, bright: false, flash: true);

        var restored = AttributeGrid.FromBytes(grid.Columns, grid.Rows, grid.ToArray());

        Assert.Equal(grid.ToArray(), restored.ToArray());
        Assert.Equal((2, 5, true, false), restored.ReadCell(0, 0));
        Assert.Equal((6, 1, false, true), restored.ReadCell(1, 1));
    }

    [Fact]
    public void Attribute_grid_rejects_cells_outside_its_bounds()
    {
        var grid = AttributeGrid.ForSprite(16, 16);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid[2, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid[0, 2]);
    }

    // --- Μάσκα ---------------------------------------------------------------

    /// <summary>
    /// Η σύμβαση του εργαλείου (1 = αδιαφανές) είναι ανάποδη από αυτή που θέλει
    /// η ρουτίνα <c>AND mask : OR data</c> του Z80. Οι δύο μέθοδοι πρέπει να
    /// δίνουν συμπληρωματικά bytes.
    /// </summary>
    [Fact]
    public void And_mask_is_the_inverse_of_the_opaque_mask()
    {
        var mask = new FrameBuffer(8, 1);
        mask[0, 0] = 1;
        mask[1, 0] = 1;

        Assert.Equal(new byte[] { 0xC0 }, MaskCodec.PackOpaque(mask));
        Assert.Equal(new byte[] { 0x3F }, MaskCodec.PackAndMask(mask));
    }

    [Fact]
    public void Mask_round_trips()
    {
        var mask = new FrameBuffer(16, 4);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                mask[x, y] = (byte)(x % 3 == 0 ? 1 : 0);
            }
        }

        var restored = MaskCodec.Unpack(MaskCodec.PackOpaque(mask), 16, 4);

        Assert.True(mask.HasSamePixels(restored));
    }

    [Fact]
    public void Applying_a_mask_clears_the_transparent_pixels()
    {
        var frame = new FrameBuffer(4, 1);
        frame[0, 0] = 1;
        frame[1, 0] = 1;
        frame[2, 0] = 1;
        frame[3, 0] = 1;

        var mask = new FrameBuffer(4, 1);
        mask[0, 0] = 1;
        mask[2, 0] = 1;

        var masked = MaskCodec.ApplyMask(frame, mask);

        Assert.Equal(1, masked[0, 0]);
        Assert.Equal(0, masked[1, 0]);
        Assert.Equal(1, masked[2, 0]);
        Assert.Equal(0, masked[3, 0]);

        // Το αρχικό καρέ δεν πειράζεται.
        Assert.Equal(1, frame[1, 0]);
    }

    [Fact]
    public void Mask_of_different_size_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => MaskCodec.ApplyMask(new FrameBuffer(8, 8), new FrameBuffer(8, 16)));
    }

    // --- Συνδυασμός: πλήρες masked sprite -----------------------------------

    /// <summary>
    /// Ολοκληρωμένος έλεγχος: ένα 8×8 sprite με μάσκα παράγει data και mask που,
    /// αν εφαρμοστούν με <c>AND</c> / <c>OR</c> πάνω σε φόντο, δίνουν το σωστό
    /// αποτέλεσμα — δηλαδή προσομοιώνουμε τι θα έκανε ο Z80.
    /// </summary>
    [Fact]
    public void Masked_sprite_composites_correctly_over_a_background()
    {
        var frame = new FrameBuffer(8, 1);
        var mask = new FrameBuffer(8, 1);

        // Αδιαφανή pixels στις θέσεις 2 και 3, με ink και στα δύο.
        frame[2, 0] = 1;
        frame[3, 0] = 1;
        mask[2, 0] = 1;
        mask[3, 0] = 1;

        var data = SpriteCodecs.For("zx.sprite").Pack(MaskCodec.ApplyMask(frame, mask))[0];
        var andMask = MaskCodec.PackAndMask(mask)[0];

        const byte background = 0xFF;
        var result = (byte)((background & andMask) | data);

        // Το φόντο διατηρείται παντού εκτός από τα δύο pixels του sprite,
        // που εδώ είναι ink (1) — άρα το byte μένει 0xFF.
        Assert.Equal(0xCF, andMask);
        Assert.Equal(0x30, data);
        Assert.Equal(0xFF, result);

        // Με μαύρο φόντο φαίνονται μόνο τα pixels του sprite.
        Assert.Equal(0x30, (byte)((0x00 & andMask) | data));
    }

    [Fact]
    public void Screen_addresses_from_the_platform_match_the_codec_row_order()
    {
        // Το export "screen layout" θα χρησιμοποιεί αυτές τις διευθύνσεις·
        // εδώ επιβεβαιώνουμε ότι διαδοχικές γραμμές sprite ΔΕΝ είναι διαδοχικές στη μνήμη.
        var first = ZxSpectrumPlatform.GetBitmapAddress(0, 0);
        var second = ZxSpectrumPlatform.GetBitmapAddress(1, 0);

        Assert.Equal(0x4000, first);
        Assert.Equal(0x4100, second);
        Assert.NotEqual(first + 1, second);
    }
}
