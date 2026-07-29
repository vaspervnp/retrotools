using RetroTools.Core.Palettes;

namespace RetroTools.Core.Platforms.Definitions;

/// <summary>
/// Sinclair ZX Spectrum 48K / 128K (ULA).
/// Χωρίς hardware sprites και χωρίς per-pixel χρώμα: κάθε κελί 8×8 έχει ένα
/// attribute byte με INK, PAPER, BRIGHT και FLASH — εξ ου και το attribute clash.
/// </summary>
public static class ZxSpectrumPlatform
{
    public const string Code = "zx";

    /// <summary>16 δείκτες (8 βασικά × BRIGHT). Οπτικά μοναδικά είναι 15: το bright black = black.</summary>
    public const int ColorCount = 16;

    public const int ScreenWidth = 256;
    public const int ScreenHeight = 192;

    public const int AttributeCellSize = 8;
    public const int AttributeColumns = ScreenWidth / AttributeCellSize;   // 32
    public const int AttributeRows = ScreenHeight / AttributeCellSize;     // 24

    /// <summary>Διεύθυνση του bitmap στη μνήμη του 48K.</summary>
    public const ushort ScreenBitmapAddress = 0x4000;

    /// <summary>Διεύθυνση των attributes.</summary>
    public const ushort ScreenAttributeAddress = 0x5800;

    private static readonly string[] BaseNames =
    {
        "Black", "Blue", "Red", "Magenta", "Green", "Cyan", "Yellow", "White",
    };

    /// <summary>
    /// Τα bits του χρώματος είναι σε σειρά <b>GRB</b>, όχι RGB:
    /// bit0 = Blue, bit1 = Red, bit2 = Green.
    /// </summary>
    public static (bool R, bool G, bool B) GetChannels(int colorIndex)
    {
        var baseColor = colorIndex & 7;
        return (R: (baseColor & 2) != 0, G: (baseColor & 4) != 0, B: (baseColor & 1) != 0);
    }

    public static bool IsBright(int colorIndex)
    {
        return colorIndex >= 8;
    }

    /// <summary>Συνθέτει attribute byte όπως το περιμένει το υλικό.</summary>
    public static byte MakeAttribute(int ink, int paper, bool bright, bool flash)
    {
        if (ink < 0 || ink > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(ink), ink, "INK: 0–7.");
        }

        if (paper < 0 || paper > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(paper), paper, "PAPER: 0–7.");
        }

        var value = (byte)(ink | (paper << 3));
        if (bright)
        {
            value |= 0x40;
        }

        if (flash)
        {
            value |= 0x80;
        }

        return value;
    }

    public static (int Ink, int Paper, bool Bright, bool Flash) ReadAttribute(byte attribute)
    {
        return (
            Ink: attribute & 0x07,
            Paper: (attribute >> 3) & 0x07,
            Bright: (attribute & 0x40) != 0,
            Flash: (attribute & 0x80) != 0);
    }

    /// <summary>
    /// Διεύθυνση του byte του bitmap για γραμμή <paramref name="y"/> και στήλη
    /// <paramref name="columnByte"/>. Η διάταξη είναι διαβόητα μη γραμμική:
    /// χωρίζεται σε τρία «τρίτα», με τις scanlines κάθε χαρακτήρα διάσπαρτες.
    /// </summary>
    public static ushort GetBitmapAddress(int y, int columnByte)
    {
        if (y < 0 || y >= ScreenHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Γραμμή: 0–191.");
        }

        if (columnByte < 0 || columnByte >= AttributeColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(columnByte), columnByte, "Στήλη byte: 0–31.");
        }

        return (ushort)(ScreenBitmapAddress
                        + ((y & 0xC0) << 5)
                        + ((y & 0x07) << 8)
                        + ((y & 0x38) << 2)
                        + columnByte);
    }

    public static ushort GetAttributeAddress(int y, int columnByte)
    {
        return (ushort)(ScreenAttributeAddress + ((y >> 3) * AttributeColumns) + columnByte);
    }

    private static PaletteProfile BuildProfile(string id, string name, string description, byte normalLevel)
    {
        var colors = new Rgb24[ColorCount];
        for (var i = 0; i < ColorCount; i++)
        {
            var channels = GetChannels(i);
            var level = IsBright(i) ? (byte)0xFF : normalLevel;

            colors[i] = new Rgb24(
                channels.R ? level : (byte)0x00,
                channels.G ? level : (byte)0x00,
                channels.B ? level : (byte)0x00);
        }

        return new PaletteProfile(id, name, description, colors);
    }

    public static HardwarePalette CreatePalette()
    {
        var colors = new HardwareColor[ColorCount];
        for (var i = 0; i < ColorCount; i++)
        {
            var name = IsBright(i) ? "Bright " + BaseNames[i & 7] : BaseNames[i & 7];
            colors[i] = HardwareColor.Simple(i, name);
        }

        // Το non-bright επίπεδο είναι ~85% της τάσης. Οι πηγές διαφωνούν αν αυτό
        // αποδίδεται ως 0xD8 ή 0xD7 — προσφέρουμε και τα δύο ώστε το preview να
        // ταιριάζει με τον emulator του χρήστη.
        var d8 = BuildProfile("d8", "Standard (0xD8)", "Non-bright επίπεδο 0xD8. Χρησιμοποιείται από το Lospec και πολλά εργαλεία pixel art.", 0xD8);
        var d7 = BuildProfile("d7", "Fuse (0xD7)", "Non-bright επίπεδο 0xD7, όπως στον emulator Fuse και σε αρκετούς άλλους.", 0xD7);

        return new HardwarePalette(Code, colors, new[] { d8, d7 }, "d8");
    }

    public static PlatformDefinition Create()
    {
        // Το πλάτος πρέπει να είναι πολλαπλάσιο του 8: ένα byte = 8 pixels και
        // οι ρουτίνες σχεδίασης δουλεύουν σε byte.
        var attributeSprite = new GraphicsMode(
            Code: "zx.sprite",
            Name: "Software sprite με attributes",
            Platform: PlatformId.ZxSpectrum,
            ScreenWidth: ScreenWidth,
            ScreenHeight: ScreenHeight,
            BitsPerPixel: 1,
            PaletteSlots: ColorCount,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerCell,
            CellWidth: AttributeCellSize,
            CellHeight: AttributeCellSize,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Aligned(8),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Ένα bitmap 1 bit/pixel συν πλέγμα attributes ceil(w/8)×ceil(h/8). " +
                   "Το BRIGHT ισχύει ταυτόχρονα για INK και PAPER του κελιού — αυτό είναι το attribute clash.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "PAPER", PixelSlotRole.PerObject, "attribute bits 3–5"),
                new PixelSlot(1, "INK", PixelSlotRole.PerObject, "attribute bits 0–2"),
            },
        };

        var monoSprite = new GraphicsMode(
            Code: "zx.sprite_mono",
            Name: "Software sprite μονόχρωμο",
            Platform: PlatformId.ZxSpectrum,
            ScreenWidth: ScreenWidth,
            ScreenHeight: ScreenHeight,
            BitsPerPixel: 1,
            PaletteSlots: 2,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerSprite,
            CellWidth: 0,
            CellHeight: 0,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Aligned(8),
            IsHardwareSprite: false,
            SupportsMask: true,
            Notes: "Μόνο bitmap, χωρίς attributes — τα χρώματα τα ορίζει το φόντο. " +
                   "Ο συνηθέστερος τύπος sprite σε παιχνίδια του Spectrum.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "Διαφανές / PAPER", PixelSlotRole.Transparent, string.Empty),
                new PixelSlot(1, "INK", PixelSlotRole.PerObject, "attribute bits 0–2"),
            },
        };

        var udg = new GraphicsMode(
            Code: "zx.udg",
            Name: "UDG — χαρακτήρας 8×8",
            Platform: PlatformId.ZxSpectrum,
            ScreenWidth: ScreenWidth,
            ScreenHeight: ScreenHeight,
            BitsPerPixel: 1,
            PaletteSlots: ColorCount,
            MaxColorsPerCell: 2,
            ColorScope: ColorScope.PerCell,
            CellWidth: AttributeCellSize,
            CellHeight: AttributeCellSize,
            PixelAspect: PixelAspect.Square,
            SpriteSize: SpriteSizeRule.Fixed(8, 8),
            IsHardwareSprite: false,
            SupportsMask: false,
            Notes: "User Defined Graphic: 8 bytes, ένα attribute. Η βάση για tile-based παιχνίδια.")
        {
            PixelSlots = new[]
            {
                new PixelSlot(0, "PAPER", PixelSlotRole.PerObject, "attribute bits 3–5"),
                new PixelSlot(1, "INK", PixelSlotRole.PerObject, "attribute bits 0–2"),
            },
        };

        return new PlatformDefinition(
            id: PlatformId.ZxSpectrum,
            code: Code,
            name: "ZX Spectrum",
            manufacturer: "Sinclair",
            year: 1982,
            cpu: CpuFamily.Z80,
            palette: CreatePalette(),
            modes: new[] { attributeSprite, monoSprite, udg },
            hasHardwareSprites: false,
            hasProgrammablePalette: false);
    }
}
