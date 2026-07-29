using RetroTools.Core.Platforms;

namespace RetroTools.Core.Codecs;

/// <summary>
/// Επιλέγει τον σωστό codec για κάθε mode. Ο CPC χρειάζεται τη δική του
/// interleaved κωδικοποίηση· όλα τα υπόλοιπα είναι MSB-first γραμμικά.
/// </summary>
public static class SpriteCodecs
{
    public static ISpriteCodec For(GraphicsMode mode)
    {
        if (mode == null)
        {
            throw new ArgumentNullException(nameof(mode));
        }

        if (mode.Platform == PlatformId.AmstradCpc)
        {
            return new CpcInterleavedCodec(mode);
        }

        return new LinearSpriteCodec(mode);
    }

    public static ISpriteCodec For(string modeCode)
    {
        return For(PlatformCatalog.GetMode(modeCode));
    }
}
