using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RetroTools.Core.Imaging;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Tests;

/// <summary>
/// Ο encoder γράφτηκε από το μηδέν, οπότε τα tests δεν αρκεί να ελέγχουν ότι
/// «βγαίνει κάτι»: αποδομούν το αρχείο σε chunks, επαληθεύουν τα CRC και
/// αποσυμπιέζουν τα δεδομένα εικόνας πίσω σε pixels.
/// </summary>
public class PngWriterTests
{
    private static readonly Rgb24[] TestPalette =
    {
        new Rgb24(0x00, 0x00, 0x00),
        new Rgb24(0xFF, 0xFF, 0xFF),
        new Rgb24(0xFF, 0x00, 0x00),
        new Rgb24(0x00, 0x80, 0xFF),
    };

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

    // --- Αποδόμηση αρχείου ---------------------------------------------------

    private sealed record Chunk(string Type, byte[] Data);

    private static List<Chunk> ReadChunks(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());

        var chunks = new List<Chunk>();
        var position = 8;

        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            var type = Encoding.ASCII.GetString(png, position + 4, 4);
            var data = png.Skip(position + 8).Take(length).ToArray();
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length, 4));

            Assert.Equal(ComputeCrc(Encoding.ASCII.GetBytes(type), data), storedCrc);

            chunks.Add(new Chunk(type, data));
            position += 12 + length;
        }

        return chunks;
    }

    private static uint ComputeCrc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in type.Concat(data))
        {
            crc ^= b;

            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] Decompress(byte[] idat)
    {
        using var input = new MemoryStream(idat);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        zlib.CopyTo(output);

        return output.ToArray();
    }

    // --- Δομή ----------------------------------------------------------------

    [Fact]
    public void Produces_the_required_chunks_in_order()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(4, 4, (x, y) => (byte)((x + y) % 4)), TestPalette);
        var chunks = ReadChunks(png);

        Assert.Equal("IHDR", chunks[0].Type);
        Assert.Equal("PLTE", chunks[1].Type);
        Assert.Equal("IDAT", chunks[2].Type);
        Assert.Equal("IEND", chunks[chunks.Count - 1].Type);
    }

    [Fact]
    public void Header_declares_indexed_eight_bit_colour()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(7, 3, (x, y) => 1), TestPalette);
        var header = ReadChunks(png).Single(c => c.Type == "IHDR").Data;

        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4)));
        Assert.Equal(8, header[8]);  // bits ανά δείκτη
        Assert.Equal(3, header[9]);  // colour type: indexed
        Assert.Equal(0, header[12]); // χωρίς interlace
    }

    [Fact]
    public void Palette_chunk_holds_three_bytes_per_colour()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), TestPalette);
        var plte = ReadChunks(png).Single(c => c.Type == "PLTE").Data;

        Assert.Equal(TestPalette.Length * 3, plte.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x80, 0xFF }, plte);
    }

    // --- Δεδομένα εικόνας ----------------------------------------------------

    [Fact]
    public void Pixel_data_round_trips_through_decompression()
    {
        var frame = MakeFrame(5, 3, (x, y) => (byte)((x * y) % 4));
        var png = PngWriter.WriteIndexed(frame, TestPalette);
        var raw = Decompress(ReadChunks(png).Single(c => c.Type == "IDAT").Data);

        // Κάθε γραμμή: 1 byte φίλτρου + 5 δείκτες.
        Assert.Equal(3 * 6, raw.Length);

        for (var y = 0; y < 3; y++)
        {
            Assert.Equal(0, raw[y * 6]); // τύπος φίλτρου

            for (var x = 0; x < 5; x++)
            {
                Assert.Equal(frame[x, y], raw[(y * 6) + 1 + x]);
            }
        }
    }

    /// <summary>
    /// Η κλίμακα είναι ο τρόπος που αποδίδεται η αναλογία pixel: ένα CPC Mode 0
    /// sprite γράφεται με scaleX διπλάσιο του scaleY, αλλιώς βγαίνει στενόμακρο.
    /// </summary>
    [Fact]
    public void Scaling_repeats_pixels_in_both_directions()
    {
        var frame = MakeFrame(2, 2, (x, y) => (byte)(x == 0 && y == 0 ? 2 : 0));
        var png = PngWriter.WriteIndexed(frame, TestPalette, scaleX: 2, scaleY: 3);

        var header = ReadChunks(png).Single(c => c.Type == "IHDR").Data;
        Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4)));
        Assert.Equal(6, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4)));

        var raw = Decompress(ReadChunks(png).Single(c => c.Type == "IDAT").Data);
        Assert.Equal(6 * 5, raw.Length); // 6 γραμμές × (1 φίλτρο + 4 pixels)

        // Το χρωματιστό pixel καταλαμβάνει 2×3 θέσεις στην πάνω αριστερή γωνία.
        for (var y = 0; y < 3; y++)
        {
            Assert.Equal(2, raw[(y * 5) + 1]);
            Assert.Equal(2, raw[(y * 5) + 2]);
            Assert.Equal(0, raw[(y * 5) + 3]);
        }

        Assert.Equal(0, raw[(3 * 5) + 1]);
    }

    // --- Διαφάνεια -----------------------------------------------------------

    [Fact]
    public void Transparent_index_produces_a_tRNS_chunk()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), TestPalette, transparentIndex: 0);
        var trns = ReadChunks(png).Single(c => c.Type == "tRNS").Data;

        Assert.Equal(new byte[] { 0 }, trns);
    }

    [Fact]
    public void Transparent_index_in_the_middle_keeps_earlier_entries_opaque()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), TestPalette, transparentIndex: 2);
        var trns = ReadChunks(png).Single(c => c.Type == "tRNS").Data;

        Assert.Equal(new byte[] { 255, 255, 0 }, trns);
    }

    [Fact]
    public void No_tRNS_chunk_when_nothing_is_transparent()
    {
        var png = PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), TestPalette);

        Assert.DoesNotContain(ReadChunks(png), c => c.Type == "tRNS");
    }

    // --- Ενσωμάτωση με τις πλατφόρμες ---------------------------------------

    [Fact]
    public void Encodes_a_real_cpc_sprite_with_its_hardware_palette()
    {
        var mode = PlatformCatalog.GetMode("cpc.mode0");
        var platform = PlatformCatalog.Get("cpc");
        var slots = DefaultPalettes.For(mode);
        var palette = slots.Select(index => platform.Palette.GetRgb(index)).ToList();

        var frame = MakeFrame(16, 16, (x, y) => (byte)((x + y) % 16));

        var png = PngWriter.WriteIndexed(
            frame,
            palette,
            scaleX: mode.PixelAspect.Width * 2,
            scaleY: mode.PixelAspect.Height * 2);

        var header = ReadChunks(png).Single(c => c.Type == "IHDR").Data;

        // Mode 0: pixels 2:1, οπότε με κλίμακα ×2 βγαίνει 64×32.
        Assert.Equal(64, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4)));
        Assert.Equal(32, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4)));
    }

    [Fact]
    public void Data_uri_is_usable_in_an_img_tag()
    {
        var uri = PngWriter.WriteDataUri(MakeFrame(4, 4, (x, y) => 1), TestPalette);

        Assert.StartsWith("data:image/png;base64,", uri, StringComparison.Ordinal);

        var bytes = Convert.FromBase64String(uri.Substring("data:image/png;base64,".Length));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
    }

    // --- Έλεγχοι ορίων -------------------------------------------------------

    [Fact]
    public void Rejects_an_empty_palette()
    {
        Assert.Throws<ArgumentException>(
            () => PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), Array.Empty<Rgb24>()));
    }

    [Fact]
    public void Rejects_a_palette_larger_than_256()
    {
        var huge = Enumerable.Range(0, 257).Select(i => new Rgb24(0, 0, 0)).ToList();

        Assert.Throws<ArgumentException>(() => PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), huge));
    }

    [Fact]
    public void Rejects_a_transparent_index_outside_the_palette()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PngWriter.WriteIndexed(MakeFrame(2, 2, (x, y) => 0), TestPalette, transparentIndex: 9));
    }
}
