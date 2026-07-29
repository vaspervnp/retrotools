namespace RetroTools.Core.Model;

/// <summary>
/// Ένα καρέ sprite ως <b>indexed buffer</b>: ένα byte ανά pixel, με τιμή τον δείκτη
/// στο palette slot του mode. Ανεξάρτητο πλατφόρμας — η μετατροπή στη μορφή του
/// υλικού γίνεται μόνο από τα codecs.
/// </summary>
public sealed class FrameBuffer
{
    private readonly byte[] _pixels;

    public FrameBuffer(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Το πλάτος πρέπει να είναι θετικό.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Το ύψος πρέπει να είναι θετικό.");
        }

        Width = width;
        Height = height;
        _pixels = new byte[width * height];
    }

    private FrameBuffer(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int PixelCount
    {
        get { return _pixels.Length; }
    }

    public byte this[int x, int y]
    {
        get
        {
            EnsureInBounds(x, y);
            return _pixels[(y * Width) + x];
        }

        set
        {
            EnsureInBounds(x, y);
            _pixels[(y * Width) + x] = value;
        }
    }

    public ReadOnlySpan<byte> Pixels
    {
        get { return _pixels; }
    }

    /// <summary>Η γραμμή <paramref name="y"/> ως span — χωρίς αντιγραφή.</summary>
    public ReadOnlySpan<byte> GetRow(int y)
    {
        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Γραμμή εκτός ορίων 0–" + (Height - 1) + ".");
        }

        return new ReadOnlySpan<byte>(_pixels, y * Width, Width);
    }

    internal Span<byte> GetWritableRow(int y)
    {
        return new Span<byte>(_pixels, y * Width, Width);
    }

    /// <summary>Δημιουργεί buffer από υπάρχοντα δεδομένα (αντιγράφει).</summary>
    public static FrameBuffer FromPixels(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException(
                "Αναμένονταν " + (width * height) + " pixels, δόθηκαν " + pixels.Length + ".",
                nameof(pixels));
        }

        return new FrameBuffer(width, height, pixels.ToArray());
    }

    public byte[] ToArray()
    {
        return (byte[])_pixels.Clone();
    }

    public FrameBuffer Clone()
    {
        return new FrameBuffer(Width, Height, (byte[])_pixels.Clone());
    }

    public void Fill(byte paletteIndex)
    {
        Array.Fill(_pixels, paletteIndex);
    }

    /// <summary>Η μεγαλύτερη τιμή pixel που υπάρχει — για έλεγχο ορίων mode.</summary>
    public byte MaxValue
    {
        get
        {
            byte max = 0;
            for (var i = 0; i < _pixels.Length; i++)
            {
                if (_pixels[i] > max)
                {
                    max = _pixels[i];
                }
            }

            return max;
        }
    }

    /// <summary>Πόσα διαφορετικά χρώματα χρησιμοποιούνται συνολικά.</summary>
    public int CountUsedColors()
    {
        var seen = new bool[256];
        var count = 0;

        for (var i = 0; i < _pixels.Length; i++)
        {
            if (!seen[_pixels[i]])
            {
                seen[_pixels[i]] = true;
                count++;
            }
        }

        return count;
    }

    public bool HasSamePixels(FrameBuffer other)
    {
        if (other == null)
        {
            return false;
        }

        return Width == other.Width
               && Height == other.Height
               && _pixels.AsSpan().SequenceEqual(other._pixels);
    }

    private void EnsureInBounds(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "Στήλη εκτός ορίων 0–" + (Width - 1) + ".");
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Γραμμή εκτός ορίων 0–" + (Height - 1) + ".");
        }
    }
}
