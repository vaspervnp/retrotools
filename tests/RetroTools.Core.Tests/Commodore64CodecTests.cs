using RetroTools.Core.Codecs;
using RetroTools.Core.Model;
using RetroTools.Core.Platforms;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class Commodore64CodecTests
{
    private static FrameBuffer Sprite(string modeCode)
    {
        var mode = PlatformCatalog.GetMode(modeCode);
        return new FrameBuffer(mode.SpriteSize.FixedWidth!.Value, mode.SpriteSize.FixedHeight!.Value);
    }

    // --- Hi-res sprite -------------------------------------------------------

    [Fact]
    public void Hires_sprite_is_exactly_63_bytes()
    {
        var codec = SpriteCodecs.For("c64.sprite_hires");
        var packed = codec.Pack(Sprite("c64.sprite_hires"));

        Assert.Equal(Commodore64Platform.SpriteDataSize, packed.Length);
        Assert.Equal(3, codec.BytesPerRow(24));
    }

    [Fact]
    public void Hires_leftmost_pixel_is_the_high_bit_of_the_first_byte()
    {
        var codec = SpriteCodecs.For("c64.sprite_hires");
        var frame = Sprite("c64.sprite_hires");
        frame[0, 0] = 1;

        var packed = codec.Pack(frame);

        Assert.Equal(0x80, packed[0]);
        Assert.Equal(0x00, packed[1]);
        Assert.Equal(0x00, packed[2]);
    }

    [Fact]
    public void Hires_rightmost_pixel_is_the_low_bit_of_the_third_byte()
    {
        var codec = SpriteCodecs.For("c64.sprite_hires");
        var frame = Sprite("c64.sprite_hires");
        frame[23, 0] = 1;

        var packed = codec.Pack(frame);

        Assert.Equal(0x00, packed[0]);
        Assert.Equal(0x00, packed[1]);
        Assert.Equal(0x01, packed[2]);
    }

    [Fact]
    public void Hires_full_row_fills_three_bytes()
    {
        var codec = SpriteCodecs.For("c64.sprite_hires");
        var frame = Sprite("c64.sprite_hires");

        for (var x = 0; x < 24; x++)
        {
            frame[x, 0] = 1;
        }

        var packed = codec.Pack(frame);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, packed.Take(3).ToArray());
        Assert.Equal(0x00, packed[3]); // η δεύτερη γραμμή είναι ακόμη άδεια
    }

    [Fact]
    public void Last_row_of_a_hires_sprite_lands_on_bytes_60_to_62()
    {
        var codec = SpriteCodecs.For("c64.sprite_hires");
        var frame = Sprite("c64.sprite_hires");
        frame[0, 20] = 1;

        var packed = codec.Pack(frame);

        Assert.Equal(0x80, packed[60]);
        Assert.Equal(62, packed.Length - 1);
    }

    // --- Multicolor sprite ---------------------------------------------------

    [Fact]
    public void Multicolor_sprite_is_also_63_bytes()
    {
        var codec = SpriteCodecs.For("c64.sprite_multicolor");

        Assert.Equal(3, codec.BytesPerRow(12));
        Assert.Equal(Commodore64Platform.SpriteDataSize, codec.Pack(Sprite("c64.sprite_multicolor")).Length);
    }

    /// <summary>
    /// Τέσσερα ζεύγη bit ανά byte, από αριστερά προς τα δεξιά:
    /// pixels 1,2,3,0 → %01 %10 %11 %00 → 0x6C.
    /// </summary>
    [Fact]
    public void Multicolor_packs_two_bits_per_pixel_msb_first()
    {
        var codec = SpriteCodecs.For("c64.sprite_multicolor");
        var frame = Sprite("c64.sprite_multicolor");

        frame[0, 0] = 1;
        frame[1, 0] = 2;
        frame[2, 0] = 3;
        frame[3, 0] = 0;

        var packed = codec.Pack(frame);

        Assert.Equal(0x6C, packed[0]);
    }

    [Fact]
    public void Multicolor_full_row_of_slot_3_is_all_ones()
    {
        var codec = SpriteCodecs.For("c64.sprite_multicolor");
        var frame = Sprite("c64.sprite_multicolor");

        for (var x = 0; x < 12; x++)
        {
            frame[x, 0] = 3;
        }

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, codec.Pack(frame).Take(3).ToArray());
    }

    // --- Χαρακτήρες ----------------------------------------------------------

    [Fact]
    public void Character_is_eight_bytes()
    {
        var codec = SpriteCodecs.For("c64.char_hires");
        var frame = new FrameBuffer(8, 8);

        for (var x = 0; x < 8; x++)
        {
            frame[x, 0] = 1;
        }

        var packed = codec.Pack(frame);

        Assert.Equal(8, packed.Length);
        Assert.Equal(0xFF, packed[0]);
    }

    // --- Round trips ---------------------------------------------------------

    [Theory]
    [InlineData("c64.sprite_hires", 24, 21)]
    [InlineData("c64.sprite_multicolor", 12, 21)]
    [InlineData("c64.char_hires", 8, 8)]
    [InlineData("c64.char_multicolor", 4, 8)]
    [InlineData("c64.bitmap_hires", 16, 16)]
    [InlineData("c64.bitmap_multicolor", 8, 16)]
    public void Pack_then_unpack_returns_the_original(string modeCode, int width, int height)
    {
        var mode = PlatformCatalog.GetMode(modeCode);
        var codec = SpriteCodecs.For(mode);
        var frame = new FrameBuffer(width, height);
        var value = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frame[x, y] = (byte)(value++ % (mode.MaxPixelValue + 1));
            }
        }

        var restored = codec.Unpack(codec.Pack(frame), width, height);

        Assert.True(frame.HasSamePixels(restored));
    }

    /// <summary>
    /// Εξαντλητικός έλεγχος μιας γραμμής multicolor: κάθε δυνατό byte πρέπει να
    /// αποκωδικοποιείται σε τέσσερα pixels και να ξανακωδικοποιείται πανομοιότυπα.
    /// </summary>
    [Fact]
    public void Every_multicolor_byte_round_trips()
    {
        var codec = SpriteCodecs.For("c64.sprite_multicolor");

        for (var value = 0; value < 256; value++)
        {
            var data = new byte[] { (byte)value };
            var frame = codec.Unpack(data, 4, 1);

            Assert.Equal(4, frame.Width);
            Assert.Equal((byte)((value >> 6) & 3), frame[0, 0]);
            Assert.Equal((byte)((value >> 4) & 3), frame[1, 0]);
            Assert.Equal((byte)((value >> 2) & 3), frame[2, 0]);
            Assert.Equal((byte)(value & 3), frame[3, 0]);

            Assert.Equal(data, codec.Pack(frame));
        }
    }

    [Fact]
    public void Colour_index_above_the_slot_count_is_rejected()
    {
        var codec = SpriteCodecs.For("c64.sprite_multicolor");
        var frame = Sprite("c64.sprite_multicolor");
        frame[0, 0] = 4;

        Assert.Throws<ArgumentException>(() => codec.Pack(frame));
    }
}
