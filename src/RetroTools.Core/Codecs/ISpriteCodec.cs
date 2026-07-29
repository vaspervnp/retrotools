using RetroTools.Core.Model;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Μετατρέπει ανάμεσα στο indexed <see cref="FrameBuffer"/> του editor και
/// στα packed bytes που περιμένει το υλικό.
/// </summary>
public interface ISpriteCodec
{
    GraphicsMode Mode { get; }

    int BytesPerRow(int width);

    int GetPackedSize(int width, int height);

    /// <summary>Γραμμή-γραμμή (row-major) — η μορφή που θέλουν οι ρουτίνες σχεδίασης.</summary>
    byte[] Pack(FrameBuffer frame);

    FrameBuffer Unpack(ReadOnlySpan<byte> data, int width, int height);
}
