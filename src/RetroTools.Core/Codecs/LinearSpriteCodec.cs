using RetroTools.Core.Platforms;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Απλή κωδικοποίηση MSB-first: το αριστερότερο pixel πηγαίνει στα ψηλότερα bits.
/// Ισχύει για ZX Spectrum (1 bit), C64 sprites/χαρακτήρες (1 και 2 bits) και CPC Mode 2.
/// Ο CPC Mode 0/1 <b>δεν</b> είναι έτσι — βλ. <see cref="CpcInterleavedCodec"/>.
/// </summary>
public sealed class LinearSpriteCodec : SpriteCodecBase
{
    private readonly int _bitsPerPixel;
    private readonly int _mask;

    public LinearSpriteCodec(GraphicsMode mode)
        : base(mode)
    {
        _bitsPerPixel = mode.BitsPerPixel;

        if (_bitsPerPixel != 1 && _bitsPerPixel != 2 && _bitsPerPixel != 4)
        {
            throw new ArgumentException(
                "Υποστηρίζονται 1, 2 ή 4 bits ανά pixel· το mode " + mode.Code +
                " δηλώνει " + _bitsPerPixel + ".",
                nameof(mode));
        }

        _mask = (1 << _bitsPerPixel) - 1;
    }

    protected override void PackRow(ReadOnlySpan<byte> pixels, Span<byte> destination)
    {
        var pixelsPerByte = Mode.PixelsPerByte;

        for (var x = 0; x < pixels.Length; x++)
        {
            var byteIndex = x / pixelsPerByte;
            var positionInByte = x % pixelsPerByte;

            // Το pixel 0 καταλαμβάνει τα υψηλότερα bits του byte.
            var shift = (pixelsPerByte - 1 - positionInByte) * _bitsPerPixel;

            destination[byteIndex] |= (byte)((pixels[x] & _mask) << shift);
        }
    }

    protected override void UnpackRow(ReadOnlySpan<byte> source, Span<byte> pixels)
    {
        var pixelsPerByte = Mode.PixelsPerByte;

        for (var x = 0; x < pixels.Length; x++)
        {
            var byteIndex = x / pixelsPerByte;
            var positionInByte = x % pixelsPerByte;
            var shift = (pixelsPerByte - 1 - positionInByte) * _bitsPerPixel;

            pixels[x] = (byte)((source[byteIndex] >> shift) & _mask);
        }
    }
}
