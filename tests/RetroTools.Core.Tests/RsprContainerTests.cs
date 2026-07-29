using RetroTools.Core.Model;
using RetroTools.Core.Serialization;

namespace RetroTools.Core.Tests;

public class RsprContainerTests
{
    private static FrameBuffer MakeFrame(int width, int height, Func<int, int, byte> pixel)
    {
        var frame = new FrameBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frame[x, y] = pixel(x, y);
            }
        }

        return frame;
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(24, 21)]
    [InlineData(16, 1)]
    [InlineData(1, 1)]
    [InlineData(128, 128)]
    public void Round_trips_at_various_sizes(int width, int height)
    {
        var frame = MakeFrame(width, height, (x, y) => (byte)((x * 7 + y * 3) % 16));

        var restored = RsprContainer.Read(RsprContainer.Write(frame));

        Assert.Equal(width, restored.Width);
        Assert.Equal(height, restored.Height);
        Assert.True(frame.HasSamePixels(restored));
    }

    [Fact]
    public void Round_trips_without_compression()
    {
        var frame = MakeFrame(16, 16, (x, y) => (byte)((x + y) % 4));

        var restored = RsprContainer.Read(RsprContainer.Write(frame, compress: false));

        Assert.True(frame.HasSamePixels(restored));
    }

    /// <summary>
    /// Το pixel art είναι εξαιρετικά συμπιέσιμο. Ένα μεγάλο μονόχρωμο sprite
    /// πρέπει να μικραίνει δραματικά, αλλιώς η επιλογή του deflate δεν αξίζει.
    /// </summary>
    [Fact]
    public void Flat_areas_compress_heavily()
    {
        var frame = new FrameBuffer(128, 128);
        frame.Fill(3);

        var written = RsprContainer.Write(frame);

        Assert.True(
            written.Length < 512,
            "Ένα ομοιόμορφο 128×128 (16 KB) συμπιέστηκε μόλις σε " + written.Length + " bytes.");
    }

    /// <summary>
    /// Σε μικρά ή θορυβώδη δεδομένα το deflate μεγαλώνει το μέγεθος.
    /// Ο container πρέπει να το καταλαβαίνει και να αποθηκεύει ασυμπίεστα.
    /// </summary>
    [Fact]
    public void Falls_back_to_uncompressed_when_deflate_does_not_help()
    {
        var frame = new FrameBuffer(2, 1);
        frame[0, 0] = 1;
        frame[1, 0] = 2;

        var written = RsprContainer.Write(frame);

        Assert.Equal(RsprContainer.HeaderSize + 2, written.Length);
        Assert.True(RsprContainer.Read(written).HasSamePixels(frame));
    }

    [Fact]
    public void Header_is_16_bytes_and_starts_with_the_magic()
    {
        var written = RsprContainer.Write(new FrameBuffer(8, 8));

        Assert.Equal(16, RsprContainer.HeaderSize);
        Assert.Equal((byte)'R', written[0]);
        Assert.Equal((byte)'S', written[1]);
        Assert.Equal((byte)'P', written[2]);
        Assert.Equal((byte)'R', written[3]);
        Assert.Equal(RsprContainer.CurrentVersion, written[4]);
    }

    [Fact]
    public void Dimensions_can_be_read_without_decompressing()
    {
        var written = RsprContainer.Write(MakeFrame(24, 21, (x, y) => (byte)(x % 3)));

        Assert.Equal((24, 21), RsprContainer.ReadDimensions(written));
    }

    // --- Ανθεκτικότητα σε χαλασμένα δεδομένα --------------------------------

    [Fact]
    public void Rejects_data_that_is_not_rspr()
    {
        var bogus = new byte[32];

        Assert.Throws<InvalidDataException>(() => RsprContainer.Read(bogus));
    }

    [Fact]
    public void Rejects_truncated_header()
    {
        Assert.Throws<InvalidDataException>(() => RsprContainer.Read(new byte[8]));
    }

    [Fact]
    public void Rejects_unknown_version()
    {
        var written = RsprContainer.Write(new FrameBuffer(8, 8));
        written[4] = 99;

        var exception = Assert.Throws<InvalidDataException>(() => RsprContainer.Read(written));

        Assert.Contains("99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_header_whose_dimensions_disagree_with_the_payload_length()
    {
        var written = RsprContainer.Write(MakeFrame(8, 8, (x, y) => (byte)x));

        // Αλλάζουμε το πλάτος σε 9 χωρίς να πειράξουμε το δηλωμένο μήκος.
        written[8] = 9;

        Assert.Throws<InvalidDataException>(() => RsprContainer.Read(written));
    }

    [Fact]
    public void Rejects_zero_dimensions()
    {
        var written = RsprContainer.Write(new FrameBuffer(8, 8));
        written[8] = 0;
        written[9] = 0;

        Assert.Throws<InvalidDataException>(() => RsprContainer.Read(written));
    }
}
