using RetroTools.Core.Palettes;

namespace RetroTools.Core.Platforms.Definitions;

/// <summary>
/// Commodore 64 (VIC-II 6567/6569).
/// Η μόνη από τις τρεις πλατφόρμες με πραγματικά hardware sprites.
/// Παλέτα: pepto.de/projects/colorvic — υπολογισμένη από ανάλυση του τσιπ.
/// </summary>
public static class Commodore64Platform
{
    public const string Code = "c64";

    public const int ColorCount = 16;

    /// <summary>Πόσα hardware sprites (MOBs) υπάρχουν ταυτόχρονα.</summary>
    public const int HardwareSpriteCount = 8;

    /// <summary>Bytes δεδομένων ανά hardware sprite: 3 bytes × 21 γραμμές.</summary>
    public const int SpriteDataSize = 63;

    /// <summary>Το sprite κάθεται σε μπλοκ 64 bytes· το 64ο byte δεν χρησιμοποιείται.</summary>
    public const int SpriteBlockSize = 64;

    private static readonly string[] ColorNames =
    {
        "Black", "White", "Red", "Cyan",
        "Purple", "Green", "Blue", "Yellow",
        "Orange", "Brown", "Light Red", "Dark Grey",
        "Grey", "Light Green", "Light Blue", "Light Grey",
    };

    /// <summary>Η παλέτα "Pepto" (PAL) — de facto πρότυπο σε emulators και εργαλεία.</summary>
    private static readonly string[] PeptoHex =
    {
        "#000000", "#FFFFFF", "#68372B", "#70A4B2",
        "#6F3D86", "#588D43", "#352879", "#B8C76F",
        "#6F4F25", "#433900", "#9A6759", "#444444",
        "#6C6C6C", "#9AD284", "#6C5EB5", "#959595",
    };

    public static HardwarePalette CreatePalette()
    {
        var colors = new HardwareColor[ColorCount];
        var pepto = new Rgb24[ColorCount];

        for (var i = 0; i < ColorCount; i++)
        {
            // Ο C64 δεν έχει programmable palette: ο δείκτης χρώματος ΕΙΝΑΙ η τιμή υλικού.
            colors[i] = HardwareColor.Simple(i, ColorNames[i]);
            pepto[i] = Rgb24.FromHex(PeptoHex[i]);
        }

        var profile = new PaletteProfile(
            "pepto",
            "Pepto (PAL)",
            "Υπολογισμένη από ανάλυση του VIC-II. Η πιο διαδεδομένη απόδοση της παλέτας του C64.",
            pepto);

        return new HardwarePalette(Code, colors, new[] { profile }, "pepto");
    }

    public static PlatformDefinition Create()
    {
        // --- Hardware sprites -------------------------------------------------
        // Hi-res: 24×21, 1 bit/pixel. Bit 0 = διαφανές, bit 1 = το χρώμα του sprite ($D027+n).
        var spriteHires = new GraphicsMode(
            Code: "c64.sprite_hires",
            Name: "Hardware sprite — hi-res 24×21",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 320,
            ScreenHeight: 200,
            BitsPerPixel: 1,
            PaletteSlots: 2,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerSprite,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Fixed(24, 21),
            IsHardwareSprite: true,
            SupportsMask: false,
            Notes: "63 bytes σε μπλοκ 64. Το μέγεθος είναι καρφωμένο από το υλικό.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Διαφανές", PixelSlotRole.Transparent, string.Empty),
                new PixelSlot(1, "Χρώμα sprite", PixelSlotRole.PerObject, "$D027+n"),
            },
        };

        // Multicolor: 12×21 δεδομένων, 2 bits/pixel, εμφανίζεται 24 pixels φαρδύ.
        var spriteMulticolor = new GraphicsMode(
            Code: "c64.sprite_multicolor",
            Name: "Hardware sprite — multicolor 12×21",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 160,
            ScreenHeight: 200,
            BitsPerPixel: 2,
            PaletteSlots: 4,
            MaxColorsPerCell: 4,
            ColorScope: ColorScope.PerSprite,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Wide,
            SpriteSize: SpriteSizeRule.Fixed(12, 21),
            IsHardwareSprite: true,
            SupportsMask: false,
            Notes: "Ίδια 63 bytes, φαρδιά pixels. ΠΡΟΣΟΧΗ: τα slots 1 και 3 είναι κοινοί " +
                   "καταχωρητές — αλλαγή τους επηρεάζει ΟΛΑ τα sprites της οθόνης.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Διαφανές", PixelSlotRole.Transparent, string.Empty),
                new PixelSlot(1, "Multicolor 0", PixelSlotRole.Shared, "$D025"),
                new PixelSlot(2, "Χρώμα sprite", PixelSlotRole.PerObject, "$D027+n"),
                new PixelSlot(3, "Multicolor 1", PixelSlotRole.Shared, "$D026"),
            },
        };

        // --- Χαρακτήρες (tiles) ----------------------------------------------
        var charHires = new GraphicsMode(
            Code: "c64.char_hires",
            Name: "Χαρακτήρας — hi-res 8×8",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 320,
            ScreenHeight: 200,
            BitsPerPixel: 1,
            PaletteSlots: 2,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerCell,
            CellWidth: 8,
            CellHeight: 8,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Fixed(8, 8),
            IsHardwareSprite: false,
            SupportsMask: false,
            Notes: "8 bytes ανά χαρακτήρα.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Background", PixelSlotRole.Shared, "$D021"),
                new PixelSlot(1, "Χρώμα χαρακτήρα", PixelSlotRole.PerObject, "Colour RAM"),
            },
        };

        var charMulticolor = new GraphicsMode(
            Code: "c64.char_multicolor",
            Name: "Χαρακτήρας — multicolor 4×8",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 160,
            ScreenHeight: 200,
            BitsPerPixel: 2,
            PaletteSlots: 4,
            MaxColorsPerCell: 4,
            ColorScope: ColorScope.PerCell,
            CellWidth: 4,
            CellHeight: 8,
            PixelAspect: PixelAspect.Wide,
            SpriteSize: SpriteSizeRule.Fixed(4, 8),
            IsHardwareSprite: false,
            SupportsMask: false,
            Notes: "Τα slots 0–2 είναι κοινά· μόνο το slot 3 είναι ανά χαρακτήρα " +
                   "και περιορίζεται στα χρώματα 0–7.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Background", PixelSlotRole.Shared, "$D021"),
                new PixelSlot(1, "Extra colour 1", PixelSlotRole.Shared, "$D022"),
                new PixelSlot(2, "Extra colour 2", PixelSlotRole.Shared, "$D023"),
                new PixelSlot(3, "Χρώμα χαρακτήρα (0–7)", PixelSlotRole.PerObject, "Colour RAM"),
            },
        };

        // --- Bitmap -----------------------------------------------------------
        var bitmapHires = new GraphicsMode(
            Code: "c64.bitmap_hires",
            Name: "Bitmap — hi-res 320×200",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 320,
            ScreenHeight: 200,
            BitsPerPixel: 1,
            PaletteSlots: 16,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerCell,
            CellWidth: 8,
            CellHeight: 8,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Aligned(8),
            IsHardwareSprite: false,
            SupportsMask: false,
            Notes: "Δύο ελεύθερα επιλεγμένα χρώματα ανά κελί 8×8, από τη Screen RAM.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Χρώμα κελιού 0", PixelSlotRole.PerObject, "Screen RAM lo-nibble"),
                new PixelSlot(1, "Χρώμα κελιού 1", PixelSlotRole.PerObject, "Screen RAM hi-nibble"),
            },
        };

        var bitmapMulticolor = new GraphicsMode(
            Code: "c64.bitmap_multicolor",
            Name: "Bitmap — multicolor 160×200",
            Platform: PlatformId.Commodore64,
            ScreenWidth: 160,
            ScreenHeight: 200,
            BitsPerPixel: 2,
            PaletteSlots: 16,
            MaxColorsPerCell: 4,
            ColorScope: ColorScope.PerCell,
            CellWidth: 4,
            CellHeight: 8,
            PixelAspect: PixelAspect.Wide,
            SpriteSize: SpriteSizeRule.Aligned(4),
            IsHardwareSprite: false,
            SupportsMask: false,
            Notes: "Ένα μόνο από τα τέσσερα χρώματα είναι κοινό· τα υπόλοιπα τρία επιλέγονται ανά κελί.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Background", PixelSlotRole.Shared, "$D021"),
                new PixelSlot(1, "Χρώμα κελιού 1", PixelSlotRole.PerObject, "Screen RAM hi-nibble"),
                new PixelSlot(2, "Χρώμα κελιού 2", PixelSlotRole.PerObject, "Screen RAM lo-nibble"),
                new PixelSlot(3, "Χρώμα κελιού 3", PixelSlotRole.PerObject, "Colour RAM"),
            },
        };

        return new PlatformDefinition(
            id: PlatformId.Commodore64,
            code: Code,
            name: "Commodore 64",
            manufacturer: "Commodore",
            year: 1982,
            cpu: CpuFamily.Mos6502,
            palette: CreatePalette(),
            modes: new[] { spriteHires, spriteMulticolor, charHires, charMulticolor, bitmapHires, bitmapMulticolor },
            hasHardwareSprites: true,
            hasProgrammablePalette: false);
    }
}
