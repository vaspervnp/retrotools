using RetroTools.Core.Imaging;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Services;

/// <summary>
/// Παράγει μικρογραφίες sprites ως <c>data:</c> URI.
/// </summary>
/// <remarks>
/// Οι εικόνες ενσωματώνονται απευθείας στο HTML αντί να σερβίρονται από endpoint:
/// ένα spritemap 8×8 θα σήμαινε 64 επιπλέον αιτήματα, το καθένα με έλεγχο
/// πρόσβασης και ερώτημα στη βάση, για δεδομένα που ο server ήδη κρατά.
/// </remarks>
public sealed class SpritePreviewService
{
    /// <summary>
    /// Η αναλογία pixel του mode εφαρμόζεται στην κλίμακα, ώστε ένα CPC Mode 0
    /// sprite να μη φαίνεται στενόμακρο στη λίστα.
    /// </summary>
    public string Render(
        SpriteFrame frame,
        Sprite sprite,
        GraphicsMode mode,
        PlatformDefinition platform,
        IReadOnlyList<int> slotColors,
        string? profileId,
        int scale = 2)
    {
        var buffer = RsprContainer.Read(frame.PixelData);
        var profile = platform.Palette.GetProfile(profileId);

        var palette = new Rgb24[slotColors.Count];
        int? transparent = null;

        for (var slot = 0; slot < slotColors.Count; slot++)
        {
            if (slot < mode.PixelSlots.Count && mode.PixelSlots[slot].Role == PixelSlotRole.Transparent)
            {
                transparent = slot;
                palette[slot] = new Rgb24(0, 0, 0);
                continue;
            }

            palette[slot] = profile[slotColors[slot]];
        }

        return PngWriter.WriteDataUri(
            buffer,
            palette,
            scale * mode.PixelAspect.Width,
            scale * mode.PixelAspect.Height,
            transparent);
    }
}
