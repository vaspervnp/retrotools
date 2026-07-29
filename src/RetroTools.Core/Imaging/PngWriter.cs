using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;

namespace RetroTools.Core.Imaging;

/// <summary>
/// Ελάχιστος encoder PNG με <b>indexed</b> χρώμα (colour type 3).
/// </summary>
/// <remarks>
/// <para>
/// Γράφτηκε αντί για εξωτερική βιβλιοθήκη εικόνας επειδή η ανάγκη είναι στενή και
/// ταιριάζει απόλυτα με τα δεδομένα μας: ένα sprite <i>είναι</i> ήδη indexed buffer
/// με παλέτα ≤ 27 χρωμάτων. Ένα γενικό imaging framework θα πρόσθετε δεκάδες MB
/// εξαρτήσεων για να κάνει ακριβώς αυτό που κάνουν 150 γραμμές εδώ.
/// </para>
/// <para>
/// Το ίδιο αρχείο εξυπηρετεί δύο σκοπούς: μικρογραφίες στο UI (ως data URI) και
/// εξαγωγή PNG (M7).
/// </para>
/// </remarks>
public static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Κωδικοποιεί ένα καρέ.
    /// </summary>
    /// <param name="frame">Το indexed buffer.</param>
    /// <param name="palette">RGB ανά δείκτη παλέτας.</param>
    /// <param name="scaleX">
    /// Οριζόντια επανάληψη pixel. Εδώ μπαίνει η αναλογία pixel της πλατφόρμας:
    /// ένα CPC Mode 0 sprite θέλει scaleX διπλάσιο του scaleY για να φαίνεται σωστά.
    /// </param>
    /// <param name="transparentIndex">Δείκτης που γράφεται με alpha 0· <c>null</c> για αδιαφανές.</param>
    public static byte[] WriteIndexed(
        FrameBuffer frame,
        IReadOnlyList<Rgb24> palette,
        int scaleX = 1,
        int scaleY = 1,
        int? transparentIndex = null)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (palette == null || palette.Count == 0)
        {
            throw new ArgumentException("Η παλέτα δεν μπορεί να είναι κενή.", nameof(palette));
        }

        if (palette.Count > 256)
        {
            throw new ArgumentException("Το indexed PNG υποστηρίζει έως 256 χρώματα.", nameof(palette));
        }

        if (scaleX < 1 || scaleY < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Η κλίμακα πρέπει να είναι τουλάχιστον 1.");
        }

        var width = frame.Width * scaleX;
        var height = frame.Height * scaleY;

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);

        WriteChunk(output, "IHDR", BuildHeader(width, height));
        WriteChunk(output, "PLTE", BuildPalette(palette));

        if (transparentIndex.HasValue)
        {
            WriteChunk(output, "tRNS", BuildTransparency(palette.Count, transparentIndex.Value));
        }

        WriteChunk(output, "IDAT", BuildImageData(frame, scaleX, scaleY));
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    /// <summary>Έτοιμο <c>data:</c> URI για ενσωμάτωση σε <c>&lt;img&gt;</c>.</summary>
    public static string WriteDataUri(
        FrameBuffer frame,
        IReadOnlyList<Rgb24> palette,
        int scaleX = 1,
        int scaleY = 1,
        int? transparentIndex = null)
    {
        var bytes = WriteIndexed(frame, palette, scaleX, scaleY, transparentIndex);

        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];

        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);

        header[8] = 8;  // bits ανά δείκτη
        header[9] = 3;  // colour type 3 = indexed
        header[10] = 0; // συμπίεση: deflate (η μόνη που ορίζει το πρότυπο)
        header[11] = 0; // μέθοδος φίλτρου
        header[12] = 0; // χωρίς interlace

        return header;
    }

    private static byte[] BuildPalette(IReadOnlyList<Rgb24> palette)
    {
        var data = new byte[palette.Count * 3];

        for (var i = 0; i < palette.Count; i++)
        {
            data[(i * 3) + 0] = palette[i].R;
            data[(i * 3) + 1] = palette[i].G;
            data[(i * 3) + 2] = palette[i].B;
        }

        return data;
    }

    /// <summary>
    /// Το tRNS δίνει alpha ανά δείκτη παλέτας. Γράφουμε μόνο μέχρι τον διαφανή
    /// δείκτη — όσοι δείκτες λείπουν θεωρούνται πλήρως αδιαφανείς.
    /// </summary>
    private static byte[] BuildTransparency(int paletteCount, int transparentIndex)
    {
        if (transparentIndex < 0 || transparentIndex >= paletteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transparentIndex), transparentIndex, "Εκτός ορίων παλέτας.");
        }

        var alpha = new byte[transparentIndex + 1];

        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = 255;
        }

        alpha[transparentIndex] = 0;

        return alpha;
    }

    private static byte[] BuildImageData(FrameBuffer frame, int scaleX, int scaleY)
    {
        var width = frame.Width * scaleX;
        var raw = new byte[(width + 1) * frame.Height * scaleY];
        var position = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.GetRow(y);

            for (var repeatY = 0; repeatY < scaleY; repeatY++)
            {
                // Κάθε γραμμή ξεκινά με το byte τύπου φίλτρου· 0 = χωρίς φίλτρο.
                raw[position++] = 0;

                for (var x = 0; x < frame.Width; x++)
                {
                    for (var repeatX = 0; repeatX < scaleX; repeatX++)
                    {
                        raw[position++] = row[x];
                    }
                }
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length, 0, length.Length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(data, 0, data.Length);

        // Το CRC καλύπτει τον τύπο και τα δεδομένα, όχι το μήκος.
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes, 0, crcBytes.Length);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;

        for (var i = 0; i < first.Length; i++)
        {
            crc = CrcTable[(crc ^ first[i]) & 0xFF] ^ (crc >> 8);
        }

        for (var i = 0; i < second.Length; i++)
        {
            crc = CrcTable[(crc ^ second[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
