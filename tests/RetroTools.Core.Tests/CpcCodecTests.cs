using RetroTools.Core.Codecs;
using RetroTools.Core.Model;
using RetroTools.Core.Platforms;
using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Tests;

public class CpcCodecTests
{
    private static CpcInterleavedCodec Codec(string modeCode)
    {
        return (CpcInterleavedCodec)SpriteCodecs.For(modeCode);
    }

    private static FrameBuffer Row(params byte[] pixels)
    {
        return FrameBuffer.FromPixels(pixels.Length, 1, pixels);
    }

    // --- Μάσκες pixel: οι κλασικοί πίνακες της τεκμηρίωσης του CPC -----------

    [Fact]
    public void Mode0_pixel_masks_are_AA_and_55()
    {
        var codec = Codec("cpc.mode0");

        Assert.Equal(0xAA, codec.GetPixelMask(0));
        Assert.Equal(0x55, codec.GetPixelMask(1));
    }

    [Fact]
    public void Mode1_pixel_masks_are_88_44_22_11()
    {
        var codec = Codec("cpc.mode1");

        Assert.Equal(0x88, codec.GetPixelMask(0));
        Assert.Equal(0x44, codec.GetPixelMask(1));
        Assert.Equal(0x22, codec.GetPixelMask(2));
        Assert.Equal(0x11, codec.GetPixelMask(3));
    }

    [Fact]
    public void Mode2_pixel_masks_are_single_bits_from_msb()
    {
        var codec = Codec("cpc.mode2");

        for (var position = 0; position < 8; position++)
        {
            Assert.Equal((byte)(0x80 >> position), codec.GetPixelMask(position));
        }
    }

    [Fact]
    public void Masks_of_all_positions_together_cover_the_whole_byte()
    {
        foreach (var modeCode in new[] { "cpc.mode0", "cpc.mode1", "cpc.mode2" })
        {
            var codec = Codec(modeCode);
            byte combined = 0;

            for (var position = 0; position < codec.Mode.PixelsPerByte; position++)
            {
                var mask = codec.GetPixelMask(position);

                // Καμία επικάλυψη ανάμεσα σε γειτονικά pixels.
                Assert.Equal(0, combined & mask);
                combined |= mask;
            }

            Assert.Equal(0xFF, combined);
        }
    }

    // --- Mode 0: κάθε bit του pen στη σωστή θέση ------------------------------

    /// <summary>
    /// Το αριστερό pixel του Mode 0: bit0→b7 (0x80), bit1→b3 (0x08),
    /// bit2→b5 (0x20), bit3→b1 (0x02).
    /// </summary>
    [Theory]
    [InlineData(1, 0x80)]
    [InlineData(2, 0x08)]
    [InlineData(4, 0x20)]
    [InlineData(8, 0x02)]
    [InlineData(15, 0xAA)]
    public void Mode0_left_pixel_pen_bits_land_in_the_documented_positions(byte pen, int expected)
    {
        var packed = Codec("cpc.mode0").Pack(Row(pen, 0));

        Assert.Single(packed);
        Assert.Equal((byte)expected, packed[0]);
    }

    [Theory]
    [InlineData(1, 0x40)]
    [InlineData(2, 0x04)]
    [InlineData(4, 0x10)]
    [InlineData(8, 0x01)]
    [InlineData(15, 0x55)]
    public void Mode0_right_pixel_pen_bits_land_one_position_lower(byte pen, int expected)
    {
        var packed = Codec("cpc.mode0").Pack(Row(0, pen));

        Assert.Equal((byte)expected, packed[0]);
    }

    [Fact]
    public void Mode0_both_pixels_at_pen_15_fill_the_byte()
    {
        Assert.Equal(new byte[] { 0xFF }, Codec("cpc.mode0").Pack(Row(15, 15)));
        Assert.Equal(new byte[] { 0x00 }, Codec("cpc.mode0").Pack(Row(0, 0)));
    }

    // --- Διασταυρούμενος έλεγχος με ανεξάρτητη υλοποίηση ---------------------

    /// <summary>
    /// Ο codec χτίζει το byte από πίνακα θέσεων. Εδώ το ίδιο byte υπολογίζεται με
    /// τον ρητό τύπο της τεκμηρίωσης (A0 B0 A2 B2 A1 B1 A3 B3). Αν οι δύο
    /// υλοποιήσεις συμφωνούν σε όλους τους 256 συνδυασμούς, ο πίνακας είναι σωστός.
    /// </summary>
    [Fact]
    public void Mode0_matches_the_reference_formula_for_all_256_pen_pairs()
    {
        var codec = Codec("cpc.mode0");

        for (var a = 0; a < 16; a++)
        {
            for (var b = 0; b < 16; b++)
            {
                var expected = (byte)(
                    ((a & 1) << 7) | ((b & 1) << 6) |
                    ((a & 4) << 3) | ((b & 4) << 2) |
                    ((a & 2) << 2) | ((b & 2) << 1) |
                    ((a & 8) >> 2) | ((b & 8) >> 3));

                var packed = codec.Pack(Row((byte)a, (byte)b));

                Assert.Equal(expected, packed[0]);
            }
        }
    }

    /// <summary>Mode 1: A0 B0 C0 D0 A1 B1 C1 D1.</summary>
    [Fact]
    public void Mode1_matches_the_reference_formula_for_all_256_pen_quads()
    {
        var codec = Codec("cpc.mode1");

        for (var packedValue = 0; packedValue < 256; packedValue++)
        {
            var a = packedValue & 3;
            var b = (packedValue >> 2) & 3;
            var c = (packedValue >> 4) & 3;
            var d = (packedValue >> 6) & 3;

            var expected = (byte)(
                ((a & 1) << 7) | ((b & 1) << 6) | ((c & 1) << 5) | ((d & 1) << 4) |
                ((a & 2) << 2) | ((b & 2) << 1) | (c & 2) | ((d & 2) >> 1));

            var actual = codec.Pack(Row((byte)a, (byte)b, (byte)c, (byte)d));

            Assert.Equal(expected, actual[0]);
        }
    }

    [Fact]
    public void Mode2_is_plain_msb_first()
    {
        var codec = Codec("cpc.mode2");

        Assert.Equal(new byte[] { 0x81 }, codec.Pack(Row(1, 0, 0, 0, 0, 0, 0, 1)));
        Assert.Equal(new byte[] { 0xFF }, codec.Pack(Row(1, 1, 1, 1, 1, 1, 1, 1)));
    }

    // --- Round trips ---------------------------------------------------------

    [Theory]
    [InlineData("cpc.mode0")]
    [InlineData("cpc.mode1")]
    [InlineData("cpc.mode2")]
    [InlineData("cpc.mode3")]
    public void Pack_then_unpack_returns_the_original(string modeCode)
    {
        var mode = PlatformCatalog.GetMode(modeCode);
        var codec = SpriteCodecs.For(mode);

        var width = mode.SpriteSize.WidthAlignment * 4;
        const int height = 12;

        var frame = new FrameBuffer(width, height);
        var value = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frame[x, y] = (byte)(value++ % (mode.MaxPixelValue + 1));
            }
        }

        var packed = codec.Pack(frame);
        var restored = codec.Unpack(packed, width, height);

        Assert.True(frame.HasSamePixels(restored));
    }

    [Fact]
    public void Mode0_16x16_sprite_is_128_bytes()
    {
        var codec = Codec("cpc.mode0");

        Assert.Equal(128, codec.GetPackedSize(16, 16));
        Assert.Equal(128, codec.Pack(new FrameBuffer(16, 16)).Length);
    }

    // --- Έλεγχοι ορίων -------------------------------------------------------

    [Fact]
    public void Pen_above_the_mode_limit_is_rejected_instead_of_corrupting_neighbours()
    {
        var codec = Codec("cpc.mode1");
        var frame = Row(0, 0, 0, 0);
        frame[1, 0] = 4; // Το Mode 1 έχει μόνο pens 0–3.

        var exception = Assert.Throws<ArgumentException>(() => codec.Pack(frame));

        Assert.Contains("0–3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mode3_allows_only_four_pens_despite_using_four_bits()
    {
        var mode = PlatformCatalog.GetMode("cpc.mode3");

        Assert.Equal(4, mode.BitsPerPixel);
        Assert.Equal(3, mode.MaxPixelValue);

        var frame = Row(0, 0);
        frame[0, 0] = 4;

        Assert.Throws<ArgumentException>(() => SpriteCodecs.For(mode).Pack(frame));
    }

    [Fact]
    public void Unpack_rejects_truncated_data()
    {
        var codec = Codec("cpc.mode0");

        Assert.Throws<ArgumentException>(() => codec.Unpack(new byte[10], 16, 16));
    }

    [Fact]
    public void Odd_width_pads_the_last_byte_with_zeroes()
    {
        // Mode 2 χωράει 8 pixels ανά byte· 12 pixels χρειάζονται 2 bytes.
        var codec = Codec("cpc.mode2");
        var frame = Row(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

        var packed = codec.Pack(frame);

        Assert.Equal(2, packed.Length);
        Assert.Equal(0xFF, packed[0]);
        Assert.Equal(0xF0, packed[1]); // 4 pixels δεδομένων + 4 bits padding
    }

    // --- Διάταξη μνήμης οθόνης ----------------------------------------------

    [Theory]
    [InlineData(0, 0, 0x0000)]
    [InlineData(1, 0, 0x0800)]
    [InlineData(7, 0, 0x3800)]
    [InlineData(8, 0, 0x0050)]
    [InlineData(8, 79, 0x009F)]
    [InlineData(199, 79, 16335)]
    public void Screen_offset_follows_the_interleaved_bank_layout(int y, int column, int expected)
    {
        Assert.Equal(expected, AmstradCpcPlatform.GetScreenOffset(y, column));
    }

    [Fact]
    public void Screen_uses_16000_of_the_16384_available_bytes()
    {
        var offsets = new HashSet<int>();

        for (var y = 0; y < 200; y++)
        {
            for (var column = 0; column < AmstradCpcPlatform.ScreenBytesPerRow; column++)
            {
                offsets.Add(AmstradCpcPlatform.GetScreenOffset(y, column));
            }
        }

        Assert.Equal(16000, offsets.Count);
        Assert.True(offsets.Max() < 16384);

        // Τα 384 bytes που περισσεύουν είναι τα γνωστά «κρυφά» bytes του CPC.
        Assert.Equal(384, 16384 - offsets.Count);
    }
}
