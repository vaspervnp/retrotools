using RetroTools.Core.Export;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Tests;

public class ExportTests
{
    private static SpriteExportSource MakeSource(
        string modeCode,
        int width,
        int height,
        string name = "player",
        Func<int, int, byte>? pixel = null)
    {
        var mode = PlatformCatalog.GetMode(modeCode);
        var platform = PlatformCatalog.Get(mode.Platform);
        var frame = new FrameBuffer(width, height);

        if (pixel != null)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    frame[x, y] = pixel(x, y);
                }
            }
        }

        return new SpriteExportSource(name, platform, mode, new[] { frame })
        {
            SlotColors = DefaultPalettes.For(mode),
        };
    }

    // --- Ταυτοποιητές --------------------------------------------------------

    [Theory]
    [InlineData("player", "player")]
    [InlineData("Ο Παίκτης", "sprite")]
    [InlineData("___", "sprite")]
    [InlineData("enemy 01", "enemy_01")]
    [InlineData("2fast", "sprite_2fast")]
    [InlineData("my-sprite!", "my_sprite")]
    public void Identifier_is_safe_for_assemblers_and_c(string name, string expected)
    {
        var source = MakeSource("cpc.mode0", 4, 4, name);

        Assert.Equal(expected, source.Identifier);
    }

    // --- Raw binary ----------------------------------------------------------

    [Fact]
    public void Binary_export_matches_the_codec_output()
    {
        var source = MakeSource("cpc.mode0", 4, 2, pixel: (x, y) => (byte)((x + y) % 16));
        var result = new BinaryExporter().Export(source, new ExportOptions());

        Assert.Equal("player.bin", result.FileName);

        // Mode 0: 2 pixels/byte → 2 bytes ανά γραμμή × 2 γραμμές.
        Assert.Equal(4, result.Content.Length);
    }

    [Fact]
    public void C64_hardware_sprite_exports_exactly_63_bytes()
    {
        var source = MakeSource("c64.sprite_hires", 24, 21);
        var result = new BinaryExporter().Export(source, new ExportOptions());

        Assert.Equal(63, result.Content.Length);
    }

    // --- PRG για VICE --------------------------------------------------------

    /// <summary>
    /// Το <c>.prg</c> ξεκινά με τη διεύθυνση φόρτωσης σε little-endian· έτσι το
    /// διαβάζει η ρουτίνα του KERNAL και ο VICE.
    /// </summary>
    [Fact]
    public void Prg_starts_with_a_little_endian_load_address()
    {
        var source = MakeSource("c64.sprite_hires", 24, 21);
        var result = new PrgExporter().Export(source, new ExportOptions { LoadAddress = 0x2000 });

        Assert.Equal("player.prg", result.FileName);
        Assert.Equal(0x00, result.Content[0]);
        Assert.Equal(0x20, result.Content[1]);
        Assert.Equal(63 + 2, result.Content.Length);
    }

    [Fact]
    public void Prg_is_offered_only_for_the_c64()
    {
        var exporter = new PrgExporter();

        Assert.True(exporter.Supports(PlatformCatalog.GetMode("c64.sprite_multicolor")));
        Assert.False(exporter.Supports(PlatformCatalog.GetMode("cpc.mode0")));
        Assert.False(exporter.Supports(PlatformCatalog.GetMode("zx.sprite")));

        Assert.Throws<InvalidOperationException>(
            () => exporter.Export(MakeSource("cpc.mode0", 4, 4), new ExportOptions()));
    }

    // --- Z80 -----------------------------------------------------------------

    [Fact]
    public void Z80_export_uses_defb_and_ampersand_hex()
    {
        var source = MakeSource("cpc.mode0", 2, 1, pixel: (x, y) => (byte)(x == 0 ? 15 : 0));
        var text = new Z80AsmExporter().Export(source, new ExportOptions()).AsText();

        Assert.Contains("player:", text, StringComparison.Ordinal);
        Assert.Contains("defb &AA", text, StringComparison.Ordinal);
        Assert.Contains("player_width_bytes equ 1", text, StringComparison.Ordinal);
        Assert.Contains("player_height      equ 1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ο προγραμματιστής χρειάζεται τις τιμές του Gate Array για να στήσει την
    /// παλέτα — όχι RGB, που δεν μπορεί να γράψει πουθενά.
    /// </summary>
    [Fact]
    public void Cpc_source_comments_list_the_gate_array_ink_values()
    {
        var source = MakeSource("cpc.mode0", 2, 2);
        var text = new Z80AsmExporter().Export(source, new ExportOptions()).AsText();

        Assert.Contains("Παλέτα:", text, StringComparison.Ordinal);
        Assert.Contains("Black", text, StringComparison.Ordinal);
        Assert.Contains("hardware &54", text, StringComparison.Ordinal); // Black = 0x54
        Assert.Contains("firmware 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Z80_exporter_covers_cpc_and_spectrum_but_not_c64()
    {
        var exporter = new Z80AsmExporter();

        Assert.True(exporter.Supports(PlatformCatalog.GetMode("cpc.mode1")));
        Assert.True(exporter.Supports(PlatformCatalog.GetMode("zx.sprite")));
        Assert.False(exporter.Supports(PlatformCatalog.GetMode("c64.sprite_hires")));
    }

    // --- 6502 / ACME ---------------------------------------------------------

    [Fact]
    public void Acme_export_uses_bang_byte_and_dollar_hex()
    {
        var source = MakeSource("c64.sprite_hires", 24, 21, pixel: (x, y) => (byte)(y == 0 ? 1 : 0));
        var text = new Acme6502Exporter().Export(source, new ExportOptions()).AsText();

        Assert.Contains("player:", text, StringComparison.Ordinal);
        Assert.Contains("!byte $ff", text, StringComparison.Ordinal);
        Assert.Contains("player_frames = 1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ο δείκτης sprite είναι διεύθυνση/64 και τα δεδομένα πρέπει να ευθυγραμμιστούν
    /// σε 64 bytes — η πιο συχνή αιτία «γιατί βλέπω σκουπίδια» στον C64.
    /// </summary>
    [Fact]
    public void Hardware_sprite_export_explains_the_64_byte_alignment()
    {
        var source = MakeSource("c64.sprite_multicolor", 12, 21);
        var text = new Acme6502Exporter().Export(source, new ExportOptions()).AsText();

        Assert.Contains("64 bytes", text, StringComparison.Ordinal);
        Assert.Contains("Sprite pointer = player / 64", text, StringComparison.Ordinal);
    }

    // --- C header ------------------------------------------------------------

    [Fact]
    public void C_header_declares_a_sized_array_and_defines()
    {
        var source = MakeSource("zx.sprite", 16, 16);
        var result = new CHeaderExporter().Export(source, new ExportOptions());
        var text = result.AsText();

        Assert.Equal("player.h", result.FileName);
        Assert.Contains("#define PLAYER_WIDTH_BYTES 2", text, StringComparison.Ordinal);
        Assert.Contains("#define PLAYER_HEIGHT 16", text, StringComparison.Ordinal);
        Assert.Contains("const unsigned char player[32] = {", text, StringComparison.Ordinal);
        Assert.Contains("};", text, StringComparison.Ordinal);
    }

    // --- Μάσκες --------------------------------------------------------------

    /// <summary>
    /// Με μάσκα, κάθε καρέ εξάγει διπλάσια bytes: δεδομένα και μετά AND-mask.
    /// </summary>
    [Fact]
    public void Mask_export_doubles_the_data_and_zeroes_transparent_pixels()
    {
        var mode = PlatformCatalog.GetMode("zx.sprite");
        var platform = PlatformCatalog.Get("zx");

        var frame = new FrameBuffer(8, 1);
        var mask = new FrameBuffer(8, 1);

        for (var x = 0; x < 8; x++)
        {
            frame[x, 0] = 1;
        }

        mask[0, 0] = 1;
        mask[1, 0] = 1;

        var source = new SpriteExportSource("masked", platform, mode, new[] { frame })
        {
            Masks = new[] { mask },
            SlotColors = DefaultPalettes.For(mode),
        };

        var withoutMask = new BinaryExporter().Export(source, new ExportOptions()).Content;
        var withMask = new BinaryExporter().Export(source, new ExportOptions { IncludeMask = true }).Content;

        Assert.Equal(new byte[] { 0xFF }, withoutMask);
        Assert.Equal(2, withMask.Length);
        Assert.Equal(0xC0, withMask[0]); // δεδομένα: μόνο τα δύο αδιαφανή pixels
        Assert.Equal(0x3F, withMask[1]); // AND-mask: 1 όπου φαίνεται το φόντο
    }

    // --- PNG -----------------------------------------------------------------

    [Fact]
    public void Png_export_applies_the_pixel_aspect_ratio()
    {
        var source = MakeSource("cpc.mode0", 8, 8);
        var result = new PngExporter().Export(source, new ExportOptions { PngScale = 4 });

        Assert.Equal("player.png", result.FileName);
        Assert.Equal("image/png", result.ContentType);

        // 8 px × κλίμακα 4 × αναλογία 2:1 → 64×32.
        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(result.Content.AsSpan(16, 4)));
        Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(result.Content.AsSpan(20, 4)));
    }

    // --- Μητρώο --------------------------------------------------------------

    [Fact]
    public void Registry_offers_only_formats_valid_for_the_mode()
    {
        var cpc = SpriteExporters.For(PlatformCatalog.GetMode("cpc.mode0")).Select(e => e.FormatId).ToList();
        var c64 = SpriteExporters.For(PlatformCatalog.GetMode("c64.sprite_hires")).Select(e => e.FormatId).ToList();

        Assert.Contains("asm-z80", cpc);
        Assert.DoesNotContain("asm-6502", cpc);
        Assert.DoesNotContain("prg", cpc);

        Assert.Contains("asm-6502", c64);
        Assert.Contains("prg", c64);
        Assert.DoesNotContain("asm-z80", c64);

        // Οι ουδέτερες μορφές είναι παντού διαθέσιμες.
        Assert.Contains("bin", cpc);
        Assert.Contains("png", c64);
    }

    [Fact]
    public void Unknown_format_lists_the_available_ones()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => SpriteExporters.Get("amiga-iff"));

        Assert.Contains("bin", exception.Message, StringComparison.Ordinal);
        Assert.Contains("png", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_registered_format_produces_content_for_a_supported_mode()
    {
        foreach (var mode in PlatformCatalog.AllModes)
        {
            var rule = mode.SpriteSize;
            var width = rule.FixedWidth ?? rule.WidthAlignment * 2;
            var height = rule.FixedHeight ?? 8;
            var source = MakeSource(mode.Code, width, height);

            foreach (var exporter in SpriteExporters.For(mode))
            {
                var result = exporter.Export(source, new ExportOptions());

                Assert.True(
                    result.Content.Length > 0,
                    mode.Code + " / " + exporter.FormatId + ": κενό αποτέλεσμα.");
                Assert.False(string.IsNullOrWhiteSpace(result.FileName));
            }
        }
    }
}
