using RetroTools.Core.Palettes;

namespace RetroTools.Core.Platforms.Definitions;

/// <summary>
/// Amstrad CPC 464 / 664 / 6128 (Gate Array 40007/40010).
/// Πηγές: cpctech.cpcwiki.de/docs/garray.html, grimware.org/documentations/devices/gatearray.
/// </summary>
public static class AmstradCpcPlatform
{
    public const string Code = "cpc";

    /// <summary>Πλήθος χρωμάτων: 3 επίπεδα (0% / 50% / 100%) στα 3 κανάλια → 3³.</summary>
    public const int ColorCount = 27;

    /// <summary>Το πρώτο ink command του Gate Array.</summary>
    public const byte FirstHardwareInk = 0x40;

    /// <summary>
    /// Ο πίνακας του υλικού: hardware ink 0x40–0x5F → firmware colour 0–26.
    /// 32 τιμές για 27 χρώματα — πέντε χρώματα έχουν δύο ισοδύναμες τιμές.
    /// Αυτή είναι η <b>μοναδική</b> δήλωση της αντιστοίχισης· όλα τα υπόλοιπα παράγονται από εδώ.
    /// </summary>
    private static readonly byte[] HardwareInkToFirmware =
    {
        /* 0x40 */ 13, /* 0x41 */ 13, /* 0x42 */ 19, /* 0x43 */ 25,
        /* 0x44 */  1, /* 0x45 */  7, /* 0x46 */ 10, /* 0x47 */ 16,
        /* 0x48 */  7, /* 0x49 */ 25, /* 0x4A */ 24, /* 0x4B */ 26,
        /* 0x4C */  6, /* 0x4D */  8, /* 0x4E */ 15, /* 0x4F */ 17,
        /* 0x50 */  1, /* 0x51 */ 19, /* 0x52 */ 18, /* 0x53 */ 20,
        /* 0x54 */  0, /* 0x55 */  2, /* 0x56 */  9, /* 0x57 */ 11,
        /* 0x58 */  4, /* 0x59 */ 22, /* 0x5A */ 21, /* 0x5B */ 23,
        /* 0x5C */  3, /* 0x5D */  5, /* 0x5E */ 12, /* 0x5F */ 14,
    };

    private static readonly string[] ColorNames =
    {
        "Black", "Blue", "Bright Blue",
        "Red", "Magenta", "Mauve",
        "Bright Red", "Purple", "Bright Magenta",
        "Green", "Cyan", "Sky Blue",
        "Yellow", "White", "Pastel Blue",
        "Orange", "Pink", "Pastel Magenta",
        "Bright Green", "Sea Green", "Bright Cyan",
        "Lime", "Pastel Green", "Pastel Cyan",
        "Bright Yellow", "Pastel Yellow", "Bright White",
    };

    /// <summary>
    /// Το firmware colour number κωδικοποιεί απευθείας τα επίπεδα RGB:
    /// <c>index = 3·R + 9·G + B</c>, με κάθε κανάλι στο 0..2.
    /// </summary>
    public static (int R, int G, int B) GetLevels(int firmwareIndex)
    {
        if (firmwareIndex < 0 || firmwareIndex >= ColorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(firmwareIndex), firmwareIndex, "Έγκυρο εύρος: 0–26.");
        }

        return (R: firmwareIndex / 3 % 3, G: firmwareIndex / 9, B: firmwareIndex % 3);
    }

    /// <summary>Όλα τα hardware inks που αντιστοιχούν σε ένα firmware colour (ένα ή δύο).</summary>
    public static IReadOnlyList<byte> GetHardwareInks(int firmwareIndex)
    {
        var result = new List<byte>(2);
        for (var i = 0; i < HardwareInkToFirmware.Length; i++)
        {
            if (HardwareInkToFirmware[i] == firmwareIndex)
            {
                result.Add((byte)(FirstHardwareInk + i));
            }
        }

        return result;
    }

    /// <summary>Bytes ανά γραμμή οθόνης — ίδιο και στα τρία modes (80 bytes = 16 KB / 200 γραμμές).</summary>
    public const int ScreenBytesPerRow = 80;

    /// <summary>Προεπιλεγμένη βάση της οθόνης στη μνήμη.</summary>
    public const ushort DefaultScreenBase = 0xC000;

    /// <summary>
    /// Θέση ενός byte οθόνης μέσα στα 16 KB. Ο CPC δεν είναι γραμμικός: οι οκτώ
    /// scanlines κάθε σειράς χαρακτήρων απέχουν 0x800 μεταξύ τους.
    /// </summary>
    public static int GetScreenOffset(int y, int columnByte, int bytesPerRow = ScreenBytesPerRow)
    {
        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Η γραμμή δεν μπορεί να είναι αρνητική.");
        }

        if (columnByte < 0 || columnByte >= bytesPerRow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnByte), columnByte, "Στήλη byte: 0–" + (bytesPerRow - 1) + ".");
        }

        return ((y & 7) * 0x800) + ((y >> 3) * bytesPerRow) + columnByte;
    }

    private static PaletteProfile BuildProfile(string id, string name, string description, byte[] levelValues)
    {
        var colors = new Rgb24[ColorCount];
        for (var i = 0; i < ColorCount; i++)
        {
            var levels = GetLevels(i);
            colors[i] = new Rgb24(levelValues[levels.R], levelValues[levels.G], levelValues[levels.B]);
        }

        return new PaletteProfile(id, name, description, colors);
    }

    public static HardwarePalette CreatePalette()
    {
        var colors = new HardwareColor[ColorCount];
        for (var i = 0; i < ColorCount; i++)
        {
            colors[i] = new HardwareColor(i, ColorNames[i], GetHardwareInks(i));
        }

        var nominal = BuildProfile(
            "nominal",
            "Nominal (0 / 128 / 255)",
            "Τα ονομαστικά επίπεδα 0% / 50% / 100%. Ό,τι δείχνουν οι περισσότεροι emulators.",
            new byte[] { 0x00, 0x80, 0xFF });

        var measured = BuildProfile(
            "measured",
            "Measured (~40% mid)",
            "Προσέγγιση του πραγματικού υλικού, όπου το μεσαίο επίπεδο μετριέται κοντά στο 40% " +
            "και όχι στο 50%. Μόνο για προβολή — οι δείκτες χρωμάτων δεν αλλάζουν.",
            new byte[] { 0x00, 0x66, 0xFF });

        return new HardwarePalette(Code, colors, new[] { nominal, measured }, "nominal");
    }

    public static PlatformDefinition Create()
    {
        // Ο CPC δεν έχει hardware sprites: όλα τα sprites είναι λογισμικού και πρέπει να
        // ευθυγραμμίζονται σε byte. Mode 0 → 2 pixels/byte, Mode 1 → 4, Mode 2 → 8.
        var mode0 = new GraphicsMode(
            Code: "cpc.mode0",
            Name: "Mode 0 — 160×200, 16 χρώματα",
            Platform: PlatformId.AmstradCpc,
            ScreenWidth: 160,
            ScreenHeight: 200,
            BitsPerPixel: 4,
            PaletteSlots: 16,
            MaxColorsPerCell: 16,
            ColorScope: ColorScope.PerPixel,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Wide,
            SpriteSize: SpriteSizeRule.Aligned(2),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Το πιο δημοφιλές mode για παιχνίδια. Φαρδιά pixels 2:1. " +
                   "Bit διάταξη στο byte: A0 B0 A2 B2 A1 B1 A3 B3.");

        var mode1 = new GraphicsMode(
            Code: "cpc.mode1",
            Name: "Mode 1 — 320×200, 4 χρώματα",
            Platform: PlatformId.AmstradCpc,
            ScreenWidth: 320,
            ScreenHeight: 200,
            BitsPerPixel: 2,
            PaletteSlots: 4,
            MaxColorsPerCell: 4,
            ColorScope: ColorScope.PerPixel,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Aligned(4),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Τετράγωνα pixels. Bit διάταξη: A0 B0 C0 D0 A1 B1 C1 D1.");

        var mode2 = new GraphicsMode(
            Code: "cpc.mode2",
            Name: "Mode 2 — 640×200, 2 χρώματα",
            Platform: PlatformId.AmstradCpc,
            ScreenWidth: 640,
            ScreenHeight: 200,
            BitsPerPixel: 1,
            PaletteSlots: 2,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerPixel,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Narrow,
            SpriteSize: SpriteSizeRule.Aligned(8),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Στενά pixels 1:2, κυρίως για κείμενο. Bit 7 = αριστερότερο pixel.");

        var mode3 = new GraphicsMode(
            Code: "cpc.mode3",
            Name: "Mode 3 — 160×200, 4 χρώματα (undocumented)",
            Platform: PlatformId.AmstradCpc,
            ScreenWidth: 160,
            ScreenHeight: 200,
            BitsPerPixel: 4,
            PaletteSlots: 4,
            MaxColorsPerCell: 4,
            ColorScope: ColorScope.PerPixel,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Wide,
            SpriteSize: SpriteSizeRule.Aligned(2),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Ατεκμηρίωτο mode. Ίδια κωδικοποίηση με το Mode 0 αλλά μόνο 4 χρησιμοποιήσιμα pens.");

        return new PlatformDefinition(
            id: PlatformId.AmstradCpc,
            code: Code,
            name: "Amstrad CPC",
            manufacturer: "Amstrad",
            year: 1984,
            cpu: CpuFamily.Z80,
            palette: CreatePalette(),
            modes: new[] { mode0, mode1, mode2, mode3 },
            hasHardwareSprites: false,
            hasProgrammablePalette: true);
    }
}
