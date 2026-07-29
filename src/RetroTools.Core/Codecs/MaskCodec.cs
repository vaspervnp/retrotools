using RetroTools.Core.Model;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Κωδικοποίηση μάσκας διαφάνειας, 1 bit ανά pixel, MSB-first.
/// </summary>
/// <remarks>
/// Σύμβαση του εργαλείου: στο <see cref="FrameBuffer"/> της μάσκας,
/// <b>1 = αδιαφανές</b> (σχεδιάζεται το sprite), <b>0 = διαφανές</b>.
/// <para>
/// Οι ρουτίνες Z80 όμως κάνουν <c>AND mask : OR data</c>, όπου το bit της μάσκας
/// πρέπει να είναι <b>1 εκεί που φαίνεται το φόντο</b> — δηλαδή ανεστραμμένο.
/// Γι' αυτό υπάρχουν δύο ξεχωριστές μέθοδοι αντί για μία «μάσκα».
/// </para>
/// </remarks>
public static class MaskCodec
{
    public static int BytesPerRow(int width)
    {
        return (width + 7) / 8;
    }

    public static int GetPackedSize(int width, int height)
    {
        return BytesPerRow(width) * height;
    }

    /// <summary>Bit = 1 εκεί που το sprite είναι αδιαφανές.</summary>
    public static byte[] PackOpaque(FrameBuffer mask)
    {
        return Pack(mask, invert: false);
    }

    /// <summary>
    /// Bit = 1 εκεί που το sprite είναι <b>διαφανές</b> — η μορφή που θέλει
    /// η εντολή <c>AND</c> σε ρουτίνα σχεδίασης Z80.
    /// </summary>
    public static byte[] PackAndMask(FrameBuffer mask)
    {
        return Pack(mask, invert: true);
    }

    public static FrameBuffer Unpack(ReadOnlySpan<byte> data, int width, int height, bool inverted = false)
    {
        var bytesPerRow = BytesPerRow(width);
        var required = bytesPerRow * height;

        if (data.Length < required)
        {
            throw new ArgumentException(
                "Χρειάζονται " + required + " bytes για μάσκα " + width + "×" + height +
                ", δόθηκαν " + data.Length + ".",
                nameof(data));
        }

        var mask = new FrameBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bit = (data[(y * bytesPerRow) + (x / 8)] >> (7 - (x % 8))) & 1;
                mask[x, y] = (byte)(inverted ? 1 - bit : bit);
            }
        }

        return mask;
    }

    /// <summary>
    /// Μηδενίζει τα διαφανή pixels του καρέ, ώστε το <c>OR data</c> να μην
    /// «λερώνει» το φόντο εκεί που το sprite δεν πρέπει να φαίνεται.
    /// </summary>
    public static FrameBuffer ApplyMask(FrameBuffer frame, FrameBuffer mask)
    {
        if (frame.Width != mask.Width || frame.Height != mask.Height)
        {
            throw new ArgumentException(
                "Η μάσκα (" + mask.Width + "×" + mask.Height + ") πρέπει να έχει τις ίδιες " +
                "διαστάσεις με το καρέ (" + frame.Width + "×" + frame.Height + ").",
                nameof(mask));
        }

        var result = frame.Clone();

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (mask[x, y] == 0)
                {
                    result[x, y] = 0;
                }
            }
        }

        return result;
    }

    private static byte[] Pack(FrameBuffer mask, bool invert)
    {
        if (mask == null)
        {
            throw new ArgumentNullException(nameof(mask));
        }

        var bytesPerRow = BytesPerRow(mask.Width);
        var output = new byte[bytesPerRow * mask.Height];

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var opaque = mask[x, y] != 0;
                var bit = invert ? !opaque : opaque;

                if (bit)
                {
                    output[(y * bytesPerRow) + (x / 8)] |= (byte)(1 << (7 - (x % 8)));
                }
            }
        }

        return output;
    }
}
