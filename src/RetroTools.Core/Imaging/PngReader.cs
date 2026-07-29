using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RetroTools.Core.Palettes;

namespace RetroTools.Core.Imaging;

/// <summary>Χρώμα με διαφάνεια, όπως βγαίνει από αποκωδικοποίηση εικόνας.</summary>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public Rgb24 ToRgb()
    {
        return new Rgb24(R, G, B);
    }
}

/// <summary>Αποκωδικοποιημένη εικόνα σε RGBA, 8 bit ανά κανάλι.</summary>
public sealed class DecodedImage
{
    private readonly byte[] _rgba;

    internal DecodedImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        _rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public Rgba32 this[int x, int y]
    {
        get
        {
            var offset = ((y * Width) + x) * 4;
            return new Rgba32(_rgba[offset], _rgba[offset + 1], _rgba[offset + 2], _rgba[offset + 3]);
        }
    }
}

/// <summary>
/// Αναγνώστης PNG.
/// </summary>
/// <remarks>
/// <para>
/// Δεν μοιράζεται κώδικα με τον <see cref="PngWriter"/>: η εγγραφή διαλέγει μία
/// κωδικοποίηση, η ανάγνωση πρέπει να δεχτεί ό,τι πετάξει οποιοδήποτε εργαλείο —
/// πέντε τύπους φίλτρων, πέντε colour types, βάθη 1 έως 16 bit.
/// </para>
/// <para>
/// Δεν υποστηρίζεται interlace (Adam7). Είναι σπάνιο σε pixel art και θα διπλασίαζε
/// την πολυπλοκότητα· απορρίπτεται ρητά αντί να παραχθεί σκουπίδι.
/// </para>
/// </remarks>
public static class PngReader
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Ανώτατο μέγεθος εικόνας — φρένο σε «zip bomb» τύπου PNG.</summary>
    public const int MaxDimension = 4096;

    private enum ColorType
    {
        Greyscale = 0,
        TrueColor = 2,
        Indexed = 3,
        GreyscaleAlpha = 4,
        TrueColorAlpha = 6,
    }

    public static DecodedImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Signature.Length || !data.Slice(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Το αρχείο δεν είναι PNG.");
        }

        int width = 0, height = 0, bitDepth = 0;
        var colorType = ColorType.TrueColor;
        byte[]? palette = null;
        byte[]? transparency = null;

        using var compressed = new MemoryStream();
        var sawHeader = false;
        var position = Signature.Length;

        while (position + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(position, 4));

            if (length < 0 || position + 12 + length > data.Length)
            {
                throw new InvalidDataException("Κατεστραμμένο PNG: chunk με μη έγκυρο μήκος.");
            }

            var type = Encoding.ASCII.GetString(data.Slice(position + 4, 4));
            var content = data.Slice(position + 8, length);

            switch (type)
            {
                case "IHDR":
                    if (length != 13)
                    {
                        throw new InvalidDataException("Κατεστραμμένο PNG: λανθασμένο IHDR.");
                    }

                    width = BinaryPrimitives.ReadInt32BigEndian(content.Slice(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(content.Slice(4, 4));
                    bitDepth = content[8];
                    colorType = (ColorType)content[9];

                    ValidateHeader(width, height, bitDepth, colorType, content[12]);
                    sawHeader = true;
                    break;

                case "PLTE":
                    palette = content.ToArray();
                    break;

                case "tRNS":
                    transparency = content.ToArray();
                    break;

                case "IDAT":
                    // Τα δεδομένα μπορεί να είναι σπασμένα σε πολλά chunks· το zlib
                    // stream είναι η ένωσή τους, όχι το καθένα ξεχωριστά.
                    compressed.Write(content);
                    break;

                case "IEND":
                    position = data.Length;
                    continue;
            }

            position += 12 + length;
        }

        if (!sawHeader)
        {
            throw new InvalidDataException("Κατεστραμμένο PNG: λείπει το IHDR.");
        }

        if (compressed.Length == 0)
        {
            throw new InvalidDataException("Κατεστραμμένο PNG: δεν βρέθηκαν δεδομένα εικόνας.");
        }

        var channels = ChannelCount(colorType);
        var bitsPerPixel = channels * bitDepth;
        var bytesPerRow = ((width * bitsPerPixel) + 7) / 8;

        var raw = Inflate(compressed.ToArray(), (bytesPerRow + 1) * height);

        if (raw.Length < (bytesPerRow + 1) * (long)height)
        {
            throw new InvalidDataException(
                "Κατεστραμμένο PNG: τα δεδομένα εικόνας είναι λιγότερα από όσα δηλώνει η επικεφαλίδα.");
        }

        var unfiltered = Unfilter(raw, width, height, bytesPerRow, Math.Max(1, bitsPerPixel / 8));

        return new DecodedImage(
            width,
            height,
            ToRgba(unfiltered, width, height, bytesPerRow, bitDepth, colorType, palette, transparency));
    }

    private static void ValidateHeader(int width, int height, int bitDepth, ColorType colorType, byte interlace)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Κατεστραμμένο PNG: μηδενικές διαστάσεις.");
        }

        if (width > MaxDimension || height > MaxDimension)
        {
            throw new InvalidDataException(
                "Η εικόνα είναι " + width + "×" + height + "· το όριο είναι " +
                MaxDimension + "×" + MaxDimension + ".");
        }

        if (interlace != 0)
        {
            throw new InvalidDataException(
                "Δεν υποστηρίζονται interlaced (Adam7) PNG. Αποθήκευσε το αρχείο χωρίς interlace.");
        }

        var validDepths = colorType switch
        {
            ColorType.Greyscale => new[] { 1, 2, 4, 8, 16 },
            ColorType.Indexed => new[] { 1, 2, 4, 8 },
            ColorType.TrueColor => new[] { 8, 16 },
            ColorType.GreyscaleAlpha => new[] { 8, 16 },
            ColorType.TrueColorAlpha => new[] { 8, 16 },
            _ => throw new InvalidDataException("Άγνωστος τύπος χρώματος PNG: " + (int)colorType + "."),
        };

        if (!validDepths.Contains(bitDepth))
        {
            throw new InvalidDataException(
                "Μη έγκυρος συνδυασμός: colour type " + (int)colorType + " με " + bitDepth + " bit.");
        }
    }

    private static int ChannelCount(ColorType colorType)
    {
        return colorType switch
        {
            ColorType.Greyscale => 1,
            ColorType.Indexed => 1,
            ColorType.TrueColor => 3,
            ColorType.GreyscaleAlpha => 2,
            ColorType.TrueColorAlpha => 4,
            _ => throw new InvalidDataException("Άγνωστος τύπος χρώματος PNG."),
        };
    }

    private static byte[] Inflate(byte[] compressed, int expectedLength)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(Math.Max(expectedLength, 1024));

        try
        {
            zlib.CopyTo(output);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("Κατεστραμμένο PNG: τα συμπιεσμένα δεδομένα δεν αποσυμπιέζονται.");
        }

        return output.ToArray();
    }

    /// <summary>
    /// Αναιρεί τα φίλτρα γραμμής. Κάθε γραμμή προηγείται από ένα byte που λέει ποιο
    /// φίλτρο εφαρμόστηκε, και η αναίρεση γίνεται <b>διαδοχικά</b>: κάθε byte
    /// χρειάζεται τα ήδη αποκατεστημένα γειτονικά του.
    /// </summary>
    private static byte[] Unfilter(byte[] raw, int width, int height, int bytesPerRow, int bytesPerPixel)
    {
        var output = new byte[bytesPerRow * height];
        var sourceStride = bytesPerRow + 1;

        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * sourceStride];
            var source = (y * sourceStride) + 1;
            var target = y * bytesPerRow;
            var previous = target - bytesPerRow;

            for (var x = 0; x < bytesPerRow; x++)
            {
                int left = x >= bytesPerPixel ? output[target + x - bytesPerPixel] : 0;
                int up = y > 0 ? output[previous + x] : 0;
                int upLeft = y > 0 && x >= bytesPerPixel ? output[previous + x - bytesPerPixel] : 0;

                var value = raw[source + x];

                output[target + x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException(
                        "Κατεστραμμένο PNG: άγνωστο φίλτρο " + filter + " στη γραμμή " + y + "."),
                };
            }
        }

        return output;
    }

    /// <summary>
    /// Ο προγνώστης Paeth: διαλέγει τον γείτονα που είναι πιο κοντά στη γραμμική
    /// πρόβλεψη <c>αριστερά + πάνω − πάνω-αριστερά</c>.
    /// </summary>
    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static byte[] ToRgba(
        byte[] pixels,
        int width,
        int height,
        int bytesPerRow,
        int bitDepth,
        ColorType colorType,
        byte[]? palette,
        byte[]? transparency)
    {
        var rgba = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var target = ((y * width) + x) * 4;
                Rgba32 color;

                switch (colorType)
                {
                    case ColorType.Indexed:
                    {
                        var index = ReadSample(pixels, (y * bytesPerRow * 8) + (x * bitDepth), bitDepth);

                        if (palette == null || (index * 3) + 2 >= palette.Length)
                        {
                            throw new InvalidDataException(
                                "Κατεστραμμένο PNG: δείκτης παλέτας " + index + " εκτός του PLTE.");
                        }

                        // Το tRNS σε indexed PNG δίνει alpha ανά δείκτη· όσοι δείκτες
                        // λείπουν από το chunk θεωρούνται αδιαφανείς.
                        var alpha = transparency != null && index < transparency.Length
                            ? transparency[index]
                            : (byte)255;

                        color = new Rgba32(palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2], alpha);
                        break;
                    }

                    case ColorType.Greyscale:
                    {
                        var sample = ReadSample(pixels, (y * bytesPerRow * 8) + (x * bitDepth), bitDepth);
                        var value = ScaleToByte(sample, bitDepth);
                        color = new Rgba32(value, value, value, 255);
                        break;
                    }

                    case ColorType.GreyscaleAlpha:
                    {
                        var step = bitDepth / 8;
                        var offset = (y * bytesPerRow) + (x * 2 * step);
                        color = new Rgba32(pixels[offset], pixels[offset], pixels[offset], pixels[offset + step]);
                        break;
                    }

                    case ColorType.TrueColor:
                    {
                        var step = bitDepth / 8;
                        var offset = (y * bytesPerRow) + (x * 3 * step);
                        color = new Rgba32(pixels[offset], pixels[offset + step], pixels[offset + (2 * step)], 255);
                        break;
                    }

                    default:
                    {
                        var step = bitDepth / 8;
                        var offset = (y * bytesPerRow) + (x * 4 * step);
                        color = new Rgba32(
                            pixels[offset],
                            pixels[offset + step],
                            pixels[offset + (2 * step)],
                            pixels[offset + (3 * step)]);
                        break;
                    }
                }

                rgba[target] = color.R;
                rgba[target + 1] = color.G;
                rgba[target + 2] = color.B;
                rgba[target + 3] = color.A;
            }
        }

        return rgba;
    }

    /// <summary>Διαβάζει δείγμα 1, 2, 4, 8 ή 16 bit από αυθαίρετη θέση bit.</summary>
    private static int ReadSample(byte[] data, int bitOffset, int bitDepth)
    {
        if (bitDepth == 8)
        {
            return data[bitOffset / 8];
        }

        if (bitDepth == 16)
        {
            // Τα 16 bit ανά κανάλι υποβιβάζονται στο υψηλό byte: η παλέτα-στόχος
            // έχει ούτως ή άλλως το πολύ 27 χρώματα.
            return data[bitOffset / 8];
        }

        var byteIndex = bitOffset / 8;
        var shift = 8 - bitDepth - (bitOffset % 8);

        return (data[byteIndex] >> shift) & ((1 << bitDepth) - 1);
    }

    private static byte ScaleToByte(int sample, int bitDepth)
    {
        return bitDepth switch
        {
            1 => (byte)(sample * 255),
            2 => (byte)(sample * 85),
            4 => (byte)(sample * 17),
            _ => (byte)sample,
        };
    }
}
