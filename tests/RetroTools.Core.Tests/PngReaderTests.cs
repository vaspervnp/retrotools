using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RetroTools.Core.Imaging;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;

namespace RetroTools.Core.Tests;

/// <summary>
/// Ο decoder πρέπει να δέχεται ό,τι πετάξει οποιοδήποτε εργαλείο. Τα tests χτίζουν
/// PNG στο χέρι, ένα ανά τύπο φίλτρου και ανά colour type, ώστε να μην ελέγχεται
/// μόνο η διαδρομή που παράγει ο δικός μας writer.
/// </summary>
public class PngReaderTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // --- Κατασκευή PNG στο χέρι ---------------------------------------------

    private static byte[] BuildPng(
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte[] rawScanlines,
        byte[]? palette = null,
        byte[]? transparency = null,
        byte interlace = 0)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = bitDepth;
        header[9] = colorType;
        header[12] = interlace;

        WriteChunk(output, "IHDR", header);

        if (palette != null)
        {
            WriteChunk(output, "PLTE", palette);
        }

        if (transparency != null)
        {
            WriteChunk(output, "tRNS", transparency);
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(rawScanlines);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = 0xFFFFFFFFu;

        foreach (var b in typeBytes.Concat(data))
        {
            crc ^= b;

            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFFu);
        stream.Write(crcBytes);
    }

    // --- Φίλτρα --------------------------------------------------------------

    /// <summary>
    /// Ίδια εικόνα 4×3 RGB, κωδικοποιημένη με κάθε έναν από τους πέντε τύπους
    /// φίλτρων. Και οι πέντε πρέπει να δώσουν πανομοιότυπο αποτέλεσμα — αυτό είναι
    /// ολόκληρο το νόημα του un-filtering.
    /// </summary>
    [Theory]
    [InlineData((byte)0)] // None
    [InlineData((byte)1)] // Sub
    [InlineData((byte)2)] // Up
    [InlineData((byte)3)] // Average
    [InlineData((byte)4)] // Paeth
    public void Every_filter_type_decodes_to_the_same_image(byte filter)
    {
        const int width = 4;
        const int height = 3;
        const int bytesPerPixel = 3;
        var stride = width * bytesPerPixel;

        // Η εικόνα-στόχος: μια ντεγκραντέ επιφάνεια, ώστε τα φίλτρα να έχουν δουλειά.
        var expected = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 3);
                expected[offset] = (byte)(20 + (x * 30));
                expected[offset + 1] = (byte)(50 + (y * 40));
                expected[offset + 2] = (byte)(200 - (x * 10) - (y * 5));
            }
        }

        var raw = ApplyFilter(expected, width, height, bytesPerPixel, filter);
        var image = PngReader.Read(BuildPng(width, height, 8, 2, raw));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 3);
                var pixel = image[x, y];

                Assert.Equal(expected[offset], pixel.R);
                Assert.Equal(expected[offset + 1], pixel.G);
                Assert.Equal(expected[offset + 2], pixel.B);
                Assert.Equal(255, pixel.A);
            }
        }
    }

    /// <summary>Εφαρμόζει φίλτρο PNG — η αντίστροφη πράξη από αυτήν του decoder.</summary>
    private static byte[] ApplyFilter(byte[] image, int width, int height, int bytesPerPixel, byte filter)
    {
        var stride = width * bytesPerPixel;
        var output = new byte[(stride + 1) * height];

        for (var y = 0; y < height; y++)
        {
            output[y * (stride + 1)] = filter;

            for (var x = 0; x < stride; x++)
            {
                int left = x >= bytesPerPixel ? image[(y * stride) + x - bytesPerPixel] : 0;
                int up = y > 0 ? image[((y - 1) * stride) + x] : 0;
                int upLeft = y > 0 && x >= bytesPerPixel ? image[((y - 1) * stride) + x - bytesPerPixel] : 0;
                var value = image[(y * stride) + x];

                var filtered = filter switch
                {
                    0 => value,
                    1 => (byte)(value - left),
                    2 => (byte)(value - up),
                    3 => (byte)(value - ((left + up) / 2)),
                    4 => (byte)(value - Paeth(left, up, upLeft)),
                    _ => value,
                };

                output[(y * (stride + 1)) + 1 + x] = filtered;
            }
        }

        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    // --- Colour types --------------------------------------------------------

    [Fact]
    public void Reads_rgba_with_alpha()
    {
        var raw = new byte[] { 0, 255, 0, 0, 128, 0, 255, 0, 255 };
        var image = PngReader.Read(BuildPng(2, 1, 8, 6, raw));

        Assert.Equal(new Rgba32(255, 0, 0, 128), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[1, 0]);
    }

    [Fact]
    public void Reads_indexed_with_transparency()
    {
        var palette = new byte[] { 0, 0, 0, 255, 255, 255, 255, 0, 0 };
        var transparency = new byte[] { 0 }; // δείκτης 0 = πλήρως διαφανής
        var raw = new byte[] { 0, 0, 1, 2 };

        var image = PngReader.Read(BuildPng(3, 1, 8, 3, raw, palette, transparency));

        Assert.Equal(0, image[0, 0].A);
        Assert.Equal(new Rgba32(255, 255, 255, 255), image[1, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[2, 0]);
    }

    [Fact]
    public void Reads_greyscale()
    {
        var raw = new byte[] { 0, 0, 128, 255 };
        var image = PngReader.Read(BuildPng(3, 1, 8, 0, raw));

        Assert.Equal(new Rgba32(0, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(128, 128, 128, 255), image[1, 0]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), image[2, 0]);
    }

    /// <summary>
    /// Βάθος κάτω από 8 bit πακετάρει πολλά pixels σε ένα byte — συνηθισμένο σε
    /// μικρά indexed PNG που παράγουν εργαλεία pixel art.
    /// </summary>
    [Fact]
    public void Reads_four_bit_indexed_where_two_pixels_share_a_byte()
    {
        var palette = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
        var raw = new byte[] { 0, 0x01, 0x23 }; // pixels 0,1,2,3

        var image = PngReader.Read(BuildPng(4, 1, 4, 3, raw, palette));

        Assert.Equal(new Rgba32(0, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[1, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), image[2, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[3, 0]);
    }

    [Fact]
    public void Reads_one_bit_greyscale()
    {
        var raw = new byte[] { 0, 0b10100000 };
        var image = PngReader.Read(BuildPng(4, 1, 1, 0, raw));

        Assert.Equal(255, image[0, 0].R);
        Assert.Equal(0, image[1, 0].R);
        Assert.Equal(255, image[2, 0].R);
        Assert.Equal(0, image[3, 0].R);
    }

    // --- Συνεργασία με τον writer -------------------------------------------

    /// <summary>
    /// Ο writer και ο reader γράφτηκαν χωριστά. Αν συμφωνούν σε πλήρη κύκλο,
    /// και οι δύο ακολουθούν το πρότυπο και όχι μια κοινή παρανόηση.
    /// </summary>
    [Fact]
    public void Round_trips_through_our_own_writer()
    {
        var palette = new[]
        {
            new Rgb24(0x00, 0x00, 0x00),
            new Rgb24(0xFF, 0x00, 0x00),
            new Rgb24(0x00, 0x80, 0xFF),
            new Rgb24(0xFF, 0xFF, 0xFF),
        };

        var frame = new FrameBuffer(9, 7);

        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                frame[x, y] = (byte)((x + y) % 4);
            }
        }

        var image = PngReader.Read(PngWriter.WriteIndexed(frame, palette));

        Assert.Equal(9, image.Width);
        Assert.Equal(7, image.Height);

        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.Equal(palette[frame[x, y]], image[x, y].ToRgb());
            }
        }
    }

    [Fact]
    public void Round_trips_transparency_through_our_own_writer()
    {
        var palette = new[] { new Rgb24(0, 0, 0), new Rgb24(255, 255, 255) };
        var frame = new FrameBuffer(2, 1);
        frame[0, 0] = 0;
        frame[1, 0] = 1;

        var image = PngReader.Read(PngWriter.WriteIndexed(frame, palette, transparentIndex: 0));

        Assert.Equal(0, image[0, 0].A);
        Assert.Equal(255, image[1, 0].A);
    }

    [Fact]
    public void Round_trips_a_scaled_image()
    {
        var palette = new[] { new Rgb24(0, 0, 0), new Rgb24(255, 0, 0) };
        var frame = new FrameBuffer(2, 2);
        frame[0, 0] = 1;

        var image = PngReader.Read(PngWriter.WriteIndexed(frame, palette, scaleX: 3, scaleY: 2));

        Assert.Equal(6, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal(new Rgb24(255, 0, 0), image[2, 1].ToRgb());
        Assert.Equal(new Rgb24(0, 0, 0), image[3, 1].ToRgb());
    }

    // --- Απόρριψη κακών αρχείων ---------------------------------------------

    [Fact]
    public void Rejects_a_file_that_is_not_png()
    {
        Assert.Throws<InvalidDataException>(() => PngReader.Read(Encoding.ASCII.GetBytes("GIF89a not a png")));
    }

    /// <summary>
    /// Το interlace απορρίπτεται ρητά αντί να παραχθεί κατεστραμμένη εικόνα —
    /// μια σιωπηλά λάθος εισαγωγή είναι χειρότερη από ένα μήνυμα λάθους.
    /// </summary>
    [Fact]
    public void Rejects_interlaced_png_with_an_actionable_message()
    {
        var png = BuildPng(2, 1, 8, 2, new byte[] { 0, 1, 2, 3, 4, 5, 6 }, interlace: 1);

        var exception = Assert.Throws<InvalidDataException>(() => PngReader.Read(png));

        Assert.Contains("interlace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_unknown_filter_type()
    {
        var png = BuildPng(2, 1, 8, 2, new byte[] { 9, 1, 2, 3, 4, 5, 6 });

        var exception = Assert.Throws<InvalidDataException>(() => PngReader.Read(png));

        Assert.Contains("φίλτρο", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_image_beyond_the_size_limit()
    {
        var png = BuildPng(PngReader.MaxDimension + 1, 1, 8, 2, new byte[] { 0 });

        var exception = Assert.Throws<InvalidDataException>(() => PngReader.Read(png));

        Assert.Contains("όριο", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_truncated_image_data()
    {
        var png = BuildPng(8, 8, 8, 2, new byte[] { 0, 1, 2, 3 });

        Assert.Throws<InvalidDataException>(() => PngReader.Read(png));
    }

    [Fact]
    public void Rejects_a_palette_index_outside_the_plte()
    {
        var palette = new byte[] { 0, 0, 0 };
        var png = BuildPng(2, 1, 8, 3, new byte[] { 0, 0, 5 }, palette);

        Assert.Throws<InvalidDataException>(() => PngReader.Read(png));
    }
}
