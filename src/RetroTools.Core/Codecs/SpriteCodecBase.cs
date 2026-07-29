using RetroTools.Core.Model;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Κοινή σκαλωσιά: έλεγχοι ορίων και επανάληψη ανά γραμμή.
/// Οι υποκλάσεις υλοποιούν μόνο την κωδικοποίηση μιας γραμμής.
/// </summary>
public abstract class SpriteCodecBase : ISpriteCodec
{
    protected SpriteCodecBase(GraphicsMode mode)
    {
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
    }

    public GraphicsMode Mode { get; }

    public int BytesPerRow(int width)
    {
        var pixelsPerByte = Mode.PixelsPerByte;
        return (width + pixelsPerByte - 1) / pixelsPerByte;
    }

    public int GetPackedSize(int width, int height)
    {
        return BytesPerRow(width) * height;
    }

    public byte[] Pack(FrameBuffer frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        ValidatePixelValues(frame);

        var bytesPerRow = BytesPerRow(frame.Width);
        var output = new byte[bytesPerRow * frame.Height];

        for (var y = 0; y < frame.Height; y++)
        {
            PackRow(frame.GetRow(y), new Span<byte>(output, y * bytesPerRow, bytesPerRow));
        }

        return output;
    }

    public FrameBuffer Unpack(ReadOnlySpan<byte> data, int width, int height)
    {
        var bytesPerRow = BytesPerRow(width);
        var required = bytesPerRow * height;

        if (data.Length < required)
        {
            throw new ArgumentException(
                "Χρειάζονται " + required + " bytes για " + width + "×" + height +
                " σε " + Mode.Code + ", δόθηκαν " + data.Length + ".",
                nameof(data));
        }

        var frame = new FrameBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            UnpackRow(data.Slice(y * bytesPerRow, bytesPerRow), frame.GetWritableRow(y));
        }

        return frame;
    }

    /// <summary>
    /// Κωδικοποιεί μία γραμμή pixels. Το <paramref name="destination"/> έχει ακριβώς
    /// <see cref="BytesPerRow"/> bytes και είναι μηδενισμένο.
    /// Τα pixels που περισσεύουν στο τελευταίο byte μένουν 0 (padding).
    /// </summary>
    protected abstract void PackRow(ReadOnlySpan<byte> pixels, Span<byte> destination);

    protected abstract void UnpackRow(ReadOnlySpan<byte> source, Span<byte> pixels);

    /// <summary>
    /// Απαγορεύει τιμές pixel εκτός των ορίων του mode. Χωρίς αυτό, ένα pen 5 σε
    /// Mode 1 θα «ξεχείλιζε» σιωπηλά σε γειτονικά pixels κατά το packing.
    /// </summary>
    private void ValidatePixelValues(FrameBuffer frame)
    {
        var max = frame.MaxValue;

        if (max > Mode.MaxPixelValue)
        {
            throw new ArgumentException(
                "Το mode " + Mode.Code + " επιτρέπει τιμές pixel 0–" + Mode.MaxPixelValue +
                ", βρέθηκε " + max + ".",
                nameof(frame));
        }
    }
}
