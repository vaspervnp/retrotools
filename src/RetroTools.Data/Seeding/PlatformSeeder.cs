using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Platforms;
using RetroTools.Data.Entities;

namespace RetroTools.Data.Seeding;

/// <summary>
/// Συγχρονίζει τους πίνακες <c>platforms</c> / <c>platform_modes</c> με τον
/// <see cref="PlatformCatalog"/> του κώδικα.
/// </summary>
/// <remarks>
/// Γίνεται σε κάθε εκκίνηση αντί για <c>HasData</c> στα migrations, ώστε μια
/// διόρθωση στα δεδομένα υλικού να μη χρειάζεται νέο migration. Τα δεδομένα
/// είναι lookup — δεν τα πειράζει ποτέ ο χρήστης.
/// </remarks>
public static class PlatformSeeder
{
    public static async Task<int> SeedAsync(RetroToolsDbContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var changes = 0;

        var existingPlatforms = await context.Platforms
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var existingModes = await context.PlatformModes
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        foreach (var platform in PlatformCatalog.All)
        {
            if (!existingPlatforms.TryGetValue(platform.Code, out var record))
            {
                record = new PlatformRecord { Code = platform.Code };
                context.Platforms.Add(record);
                changes++;
            }

            record.Name = platform.Name;
            record.Manufacturer = platform.Manufacturer;
            record.Year = platform.Year;
            record.ColorCount = platform.Palette.Count;
            record.HasHardwareSprites = platform.HasHardwareSprites;
            record.HasProgrammablePalette = platform.HasProgrammablePalette;

            foreach (var mode in platform.Modes)
            {
                if (!existingModes.TryGetValue(mode.Code, out var modeRecord))
                {
                    modeRecord = new PlatformModeRecord { Code = mode.Code };
                    context.PlatformModes.Add(modeRecord);
                    changes++;
                }

                modeRecord.PlatformCode = platform.Code;
                modeRecord.Name = mode.Name;
                modeRecord.ScreenWidth = mode.ScreenWidth;
                modeRecord.ScreenHeight = mode.ScreenHeight;
                modeRecord.BitsPerPixel = mode.BitsPerPixel;
                modeRecord.PaletteSlots = mode.PaletteSlots;
                modeRecord.MaxColorsPerCell = mode.MaxColorsPerCell;
                modeRecord.ColorScope = (int)mode.ColorScope;
                modeRecord.CellWidth = mode.CellWidth;
                modeRecord.CellHeight = mode.CellHeight;
                modeRecord.PixelAspectWidth = mode.PixelAspect.Width;
                modeRecord.PixelAspectHeight = mode.PixelAspect.Height;
                modeRecord.WidthAlignment = mode.SpriteSize.WidthAlignment;
                modeRecord.HeightAlignment = mode.SpriteSize.HeightAlignment;
                modeRecord.FixedWidth = mode.SpriteSize.FixedWidth;
                modeRecord.FixedHeight = mode.SpriteSize.FixedHeight;
                modeRecord.IsHardwareSprite = mode.IsHardwareSprite;
                modeRecord.SupportsMask = mode.SupportsMask;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return changes;
    }
}
