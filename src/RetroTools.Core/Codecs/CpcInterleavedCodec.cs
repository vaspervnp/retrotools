using RetroTools.Core.Platforms;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Η κωδικοποίηση pixel του Amstrad CPC. Τα bits ενός pen <b>δεν</b> κάθονται μαζί
/// μέσα στο byte — είναι διάσπαρτα, κάτι που κάνει το Mode 0 packing την πιο συχνή
/// πηγή λαθών σε εργαλεία CPC.
/// </summary>
/// <remarks>
/// <para>Mode 0 (2 pixels/byte, A αριστερά): <c>A0 B0 A2 B2 A1 B1 A3 B3</c></para>
/// <para>Mode 1 (4 pixels/byte): <c>A0 B0 C0 D0 A1 B1 C1 D1</c></para>
/// <para>Mode 2 (8 pixels/byte): <c>A B C D E F G H</c></para>
/// <para>
/// Και τα τρία περιγράφονται από έναν κανόνα: το bit <c>k</c> του pen ενός pixel
/// στη θέση <c>p</c> πηγαίνει στο bit <c>BitPositions[k] - p</c> του byte, με
/// <c>BitPositions = { 7, 3, 5, 1 }</c>. Το Mode 2 προκύπτει ως ειδική περίπτωση.
/// </para>
/// </remarks>
public sealed class CpcInterleavedCodec : SpriteCodecBase
{
    /// <summary>Θέση του bit 0/1/2/3 του pen για το αριστερότερο pixel του byte.</summary>
    private static readonly int[] BitPositions = { 7, 3, 5, 1 };

    private readonly int _bitsPerPixel;

    public CpcInterleavedCodec(GraphicsMode mode)
        : base(mode)
    {
        if (mode.Platform != PlatformId.AmstradCpc)
        {
            throw new ArgumentException(
                "Ο codec αφορά μόνο τον Amstrad CPC· δόθηκε " + mode.Code + ".",
                nameof(mode));
        }

        _bitsPerPixel = mode.BitsPerPixel;

        if (_bitsPerPixel < 1 || _bitsPerPixel > 4)
        {
            throw new ArgumentException(
                "Ο CPC χρησιμοποιεί 1, 2 ή 4 bits ανά pixel· το mode " + mode.Code +
                " δηλώνει " + _bitsPerPixel + ".",
                nameof(mode));
        }
    }

    protected override void PackRow(ReadOnlySpan<byte> pixels, Span<byte> destination)
    {
        var pixelsPerByte = Mode.PixelsPerByte;

        for (var x = 0; x < pixels.Length; x++)
        {
            var byteIndex = x / pixelsPerByte;
            var position = x % pixelsPerByte;
            var pen = pixels[x];

            for (var bit = 0; bit < _bitsPerPixel; bit++)
            {
                if ((pen & (1 << bit)) == 0)
                {
                    continue;
                }

                destination[byteIndex] |= (byte)(1 << (BitPositions[bit] - position));
            }
        }
    }

    protected override void UnpackRow(ReadOnlySpan<byte> source, Span<byte> pixels)
    {
        var pixelsPerByte = Mode.PixelsPerByte;

        for (var x = 0; x < pixels.Length; x++)
        {
            var byteIndex = x / pixelsPerByte;
            var position = x % pixelsPerByte;
            var value = source[byteIndex];
            byte pen = 0;

            for (var bit = 0; bit < _bitsPerPixel; bit++)
            {
                if ((value & (1 << (BitPositions[bit] - position))) != 0)
                {
                    pen |= (byte)(1 << bit);
                }
            }

            pixels[x] = pen;
        }
    }

    /// <summary>
    /// Η μάσκα bits που καταλαμβάνει ένα pixel στη θέση <paramref name="position"/>
    /// μέσα στο byte. Χρήσιμο για τεκμηρίωση και για τον έλεγχο του πίνακα.
    /// </summary>
    public byte GetPixelMask(int position)
    {
        if (position < 0 || position >= Mode.PixelsPerByte)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "Θέση 0–" + (Mode.PixelsPerByte - 1) + " για το " + Mode.Code + ".");
        }

        byte mask = 0;
        for (var bit = 0; bit < _bitsPerPixel; bit++)
        {
            mask |= (byte)(1 << (BitPositions[bit] - position));
        }

        return mask;
    }
}
