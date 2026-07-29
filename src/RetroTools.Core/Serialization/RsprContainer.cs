using System.Buffers.Binary;
using System.IO.Compression;
using RetroTools.Core.Model;

namespace RetroTools.Core.Serialization;

/// <summary>
/// Κωδικοποίηση των pixel δεδομένων ενός καρέ για αποθήκευση σε <c>MEDIUMBLOB</c>.
/// </summary>
/// <remarks>
/// <para>
/// Στη βάση αποθηκεύεται πάντα το <b>indexed</b> buffer (1 byte ανά pixel), όχι
/// packed δεδομένα πλατφόρμας: έτσι η αλλαγή mode ή παλέτας δεν απαιτεί μετατροπή
/// και το undo/redo παραμένει απλό.
/// </para>
/// <para>
/// Επικεφαλίδα 16 bytes:
/// <c>"RSPR"</c> (4) · version (1) · flags (1) · encoding (1) · reserved (1) ·
/// width (2, LE) · height (2, LE) · uncompressed length (4, LE).
/// Το header επιτρέπει αλλαγή κωδικοποίησης στο μέλλον χωρίς migration.
/// </para>
/// </remarks>
public static class RsprContainer
{
    public const int HeaderSize = 16;

    public const byte CurrentVersion = 1;

    private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'P', (byte)'R' };

    [Flags]
    private enum ContainerFlags : byte
    {
        None = 0,
        Deflate = 1,
    }

    private enum Encoding : byte
    {
        /// <summary>Ένα byte ανά pixel, row-major.</summary>
        Indexed8 = 0,
    }

    /// <summary>
    /// Σειριοποιεί ένα buffer. Η συμπίεση εφαρμόζεται μόνο αν πραγματικά κερδίζει
    /// χώρο — σε μικρά sprites το deflate μπορεί να μεγαλώσει τα δεδομένα.
    /// </summary>
    public static byte[] Write(FrameBuffer frame, bool compress = true)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var raw = frame.ToArray();
        var payload = raw;
        var flags = ContainerFlags.None;

        if (compress)
        {
            var compressed = Deflate(raw);
            if (compressed.Length < raw.Length)
            {
                payload = compressed;
                flags = ContainerFlags.Deflate;
            }
        }

        var result = new byte[HeaderSize + payload.Length];
        var span = result.AsSpan();

        Magic.CopyTo(span);
        span[4] = CurrentVersion;
        span[5] = (byte)flags;
        span[6] = (byte)Encoding.Indexed8;
        span[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8, 2), checked((ushort)frame.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), checked((ushort)frame.Height));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12, 4), raw.Length);

        payload.CopyTo(span.Slice(HeaderSize));

        return result;
    }

    public static FrameBuffer Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(
                "Πολύ μικρά δεδομένα για RSPR: " + data.Length + " bytes, ελάχιστο " + HeaderSize + ".");
        }

        if (!data.Slice(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Τα δεδομένα δεν είναι RSPR container (λάθος magic).");
        }

        var version = data[4];
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                "Μη υποστηριζόμενη έκδοση RSPR: " + version + " (υποστηρίζεται " + CurrentVersion + ").");
        }

        var flags = (ContainerFlags)data[5];
        var encoding = (Encoding)data[6];

        if (encoding != Encoding.Indexed8)
        {
            throw new InvalidDataException("Μη υποστηριζόμενη κωδικοποίηση RSPR: " + (byte)encoding + ".");
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        var rawLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4));

        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("Το RSPR δηλώνει μηδενικές διαστάσεις.");
        }

        if (rawLength != width * height)
        {
            throw new InvalidDataException(
                "Ασυνεπές RSPR: διαστάσεις " + width + "×" + height + " απαιτούν " +
                (width * height) + " bytes, το header δηλώνει " + rawLength + ".");
        }

        var payload = data.Slice(HeaderSize);
        var pixels = (flags & ContainerFlags.Deflate) != 0
            ? Inflate(payload, rawLength)
            : payload.ToArray();

        if (pixels.Length != rawLength)
        {
            throw new InvalidDataException(
                "Το RSPR περιέχει " + pixels.Length + " bytes ενώ αναμένονταν " + rawLength + ".");
        }

        return FrameBuffer.FromPixels(width, height, pixels);
    }

    /// <summary>Διαβάζει μόνο τις διαστάσεις, χωρίς αποσυμπίεση — για γρήγορες λίστες.</summary>
    public static (int Width, int Height) ReadDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data.Slice(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Τα δεδομένα δεν είναι RSPR container.");
        }

        return (
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)));
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static byte[] Inflate(ReadOnlySpan<byte> payload, int expectedLength)
    {
        using var input = new MemoryStream(payload.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedLength);

        deflate.CopyTo(output);

        return output.ToArray();
    }
}
