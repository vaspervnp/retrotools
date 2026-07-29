using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Web.Models;

/// <summary>
/// Ό,τι χρειάζεται ο editor για να επιβάλλει τους σωστούς περιορισμούς και να
/// δείξει σωστά χρώματα, σε ένα αίτημα. Τα δεδομένα είναι στατικά.
/// </summary>
public sealed record PlatformDto(
    string Code,
    string Name,
    string Manufacturer,
    int Year,
    string Cpu,
    bool HasHardwareSprites,
    bool HasProgrammablePalette,
    PaletteDto Palette,
    IReadOnlyList<GraphicsModeDto> Modes)
{
    public static PlatformDto From(PlatformDefinition platform)
    {
        return new PlatformDto(
            platform.Code,
            platform.Name,
            platform.Manufacturer,
            platform.Year,
            platform.Cpu.ToString(),
            platform.HasHardwareSprites,
            platform.HasProgrammablePalette,
            PaletteDto.From(platform.Palette),
            platform.Modes.Select(GraphicsModeDto.From).ToList());
    }
}

public sealed record PaletteDto(
    int ColorCount,
    int DistinctColorCount,
    string DefaultProfileId,
    IReadOnlyList<PaletteProfileDto> Profiles,
    IReadOnlyList<HardwareColorDto> Colors)
{
    public static PaletteDto From(HardwarePalette palette)
    {
        return new PaletteDto(
            palette.Count,
            palette.CountDistinctColors(),
            palette.DefaultProfile.Id,
            palette.Profiles.Select(p => new PaletteProfileDto(
                p.Id,
                p.Name,
                p.Description,
                p.Colors.Select(c => c.ToHex()).ToList())).ToList(),
            palette.Colors.Select(c => new HardwareColorDto(
                c.Index,
                c.Name,
                c.HardwareValues.ToList())).ToList());
    }
}

public sealed record PaletteProfileDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Colors);

/// <param name="HardwareValues">
/// Οι τιμές που γράφονται στο υλικό. Στον CPC κάποια χρώματα έχουν δύο ισοδύναμα
/// Gate Array inks — γι' αυτό είναι λίστα και όχι ένας αριθμός.
/// </param>
public sealed record HardwareColorDto(int Index, string Name, IReadOnlyList<byte> HardwareValues);

public sealed record GraphicsModeDto(
    string Code,
    string Name,
    string PlatformCode,
    int ScreenWidth,
    int ScreenHeight,
    int BitsPerPixel,
    int PaletteSlots,
    int MaxColorsPerCell,
    string ColorScope,
    int CellWidth,
    int CellHeight,
    int PixelAspectWidth,
    int PixelAspectHeight,
    int MaxPixelValue,
    int PixelsPerByte,
    SpriteSizeDto SpriteSize,
    bool IsHardwareSprite,
    bool SupportsMask,
    IReadOnlyList<PixelSlotDto> PixelSlots,
    string Notes)
{
    public static GraphicsModeDto From(GraphicsMode mode)
    {
        return new GraphicsModeDto(
            mode.Code,
            mode.Name,
            mode.Code.Split('.')[0],
            mode.ScreenWidth,
            mode.ScreenHeight,
            mode.BitsPerPixel,
            mode.PaletteSlots,
            mode.MaxColorsPerCell,
            mode.ColorScope.ToString(),
            mode.CellWidth,
            mode.CellHeight,
            mode.PixelAspect.Width,
            mode.PixelAspect.Height,
            mode.MaxPixelValue,
            mode.PixelsPerByte,
            SpriteSizeDto.From(mode.SpriteSize),
            mode.IsHardwareSprite,
            mode.SupportsMask,
            mode.PixelSlots.Select(s => new PixelSlotDto(
                s.Index,
                s.Name,
                s.Role.ToString(),
                s.HardwareRegister,
                s.IsGlobal)).ToList(),
            mode.Notes);
    }
}

public sealed record SpriteSizeDto(
    int WidthAlignment,
    int HeightAlignment,
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    int? FixedWidth,
    int? FixedHeight,
    bool IsFixed)
{
    public static SpriteSizeDto From(SpriteSizeRule rule)
    {
        return new SpriteSizeDto(
            rule.WidthAlignment,
            rule.HeightAlignment,
            rule.MinWidth,
            rule.MinHeight,
            rule.MaxWidth,
            rule.MaxHeight,
            rule.FixedWidth,
            rule.FixedHeight,
            rule.IsFixed);
    }
}

/// <param name="IsGlobal">
/// Αν είναι true, η αλλαγή αυτού του χρώματος επηρεάζει <b>όλα</b> τα sprites —
/// το UI πρέπει να το δείχνει καθαρά.
/// </param>
public sealed record PixelSlotDto(
    int Index,
    string Name,
    string Role,
    string HardwareRegister,
    bool IsGlobal);
