using System.Globalization;
using System.Text;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Export;

/// <summary>Κοινή σκαλωσιά για exporters πηγαίου κώδικα.</summary>
public abstract class SourceExporterBase : ISpriteExporter
{
    public abstract string FormatId { get; }

    public abstract string DisplayName { get; }

    public abstract bool Supports(GraphicsMode mode);

    public ExportResult Export(SpriteExportSource source, ExportOptions options)
    {
        if (!Supports(source.Mode))
        {
            throw new InvalidOperationException(
                "Ο exporter '" + FormatId + "' δεν υποστηρίζει το mode " + source.Mode.Code + ".");
        }

        var builder = new StringBuilder();
        WriteHeader(builder, source, options);
        WriteData(builder, source, options);

        return new ExportResult(
            source.Identifier + Extension,
            "text/plain; charset=utf-8",
            Encoding.UTF8.GetBytes(builder.ToString()));
    }

    protected abstract string Extension { get; }

    protected abstract string CommentPrefix { get; }

    protected abstract string ByteDirective { get; }

    protected abstract string FormatByte(byte value);

    protected virtual void WriteHeader(StringBuilder builder, SpriteExportSource source, ExportOptions options)
    {
        var bytesPerRow = SpriteBytes.BytesPerRow(source);
        var frameBytes = bytesPerRow * source.Height;

        builder.Append(CommentPrefix).Append(' ').Append(source.Name).AppendLine();
        builder.Append(CommentPrefix).Append(' ').Append(source.Platform.Name)
            .Append(" — ").Append(source.Mode.Name).AppendLine();
        builder.Append(CommentPrefix).Append(' ')
            .Append(source.Width).Append('x').Append(source.Height).Append(" pixels · ")
            .Append(bytesPerRow).Append(" bytes/γραμμή · ")
            .Append(frameBytes).Append(" bytes/καρέ · ")
            .Append(source.Frames.Count).Append(" καρέ").AppendLine();

        if (options.IncludeMask && source.Masks.Count > 0)
        {
            builder.Append(CommentPrefix)
                .Append(" Κάθε καρέ: δεδομένα, μετά μάσκα AND (bit 1 = φαίνεται το φόντο).")
                .AppendLine();
        }

        WritePaletteComment(builder, source);
        builder.AppendLine();
    }

    /// <summary>
    /// Η παλέτα γράφεται ως σχόλιο με τις <b>τιμές υλικού</b>, όχι με RGB: αυτό
    /// χρειάζεται ο προγραμματιστής για να στήσει την οθόνη πριν σχεδιάσει το sprite.
    /// </summary>
    private void WritePaletteComment(StringBuilder builder, SpriteExportSource source)
    {
        if (source.SlotColors.Count == 0)
        {
            return;
        }

        builder.Append(CommentPrefix).Append(" Παλέτα:").AppendLine();

        for (var slot = 0; slot < source.SlotColors.Count; slot++)
        {
            var colorIndex = source.SlotColors[slot];
            var color = source.Platform.Palette[colorIndex];
            var slotName = slot < source.Mode.PixelSlots.Count ? source.Mode.PixelSlots[slot].Name : "slot " + slot;

            builder.Append(CommentPrefix)
                .Append("   ").Append(slot).Append(": ").Append(slotName)
                .Append(" = ").Append(color.Name).Append(" (");

            if (source.Platform.Id == PlatformId.AmstradCpc)
            {
                builder.Append("firmware ").Append(colorIndex)
                    .Append(", hardware ")
                    .Append(string.Join("/", color.HardwareValues.Select(v => "&" + v.ToString("X2", CultureInfo.InvariantCulture))));
            }
            else
            {
                builder.Append("colour ").Append(colorIndex);
            }

            builder.Append(')').AppendLine();
        }
    }

    protected virtual void WriteData(StringBuilder builder, SpriteExportSource source, ExportOptions options)
    {
        var data = SpriteBytes.Pack(source, options);
        var bytesPerRow = SpriteBytes.BytesPerRow(source);

        WriteEquates(builder, source, bytesPerRow);

        builder.Append(source.Identifier).AppendLine(":");

        var perLine = Math.Max(1, options.BytesPerLine);

        for (var offset = 0; offset < data.Length; offset += perLine)
        {
            var count = Math.Min(perLine, data.Length - offset);

            builder.Append('\t').Append(ByteDirective).Append(' ');

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(FormatByte(data[offset + i]));
            }

            builder.AppendLine();
        }
    }

    protected abstract void WriteEquates(StringBuilder builder, SpriteExportSource source, int bytesPerRow);
}

/// <summary>
/// Z80 για <b>rasm</b> (επιλογή χρήστη). Η ίδια σύνταξη γίνεται δεκτή και από
/// sjasmplus και pasmo, οπότε το αρχείο είναι χρήσιμο και εκεί.
/// </summary>
public sealed class Z80AsmExporter : SourceExporterBase
{
    public override string FormatId
    {
        get { return "asm-z80"; }
    }

    public override string DisplayName
    {
        get { return "Z80 assembly (rasm)"; }
    }

    public override bool Supports(GraphicsMode mode)
    {
        return mode.Platform == PlatformId.AmstradCpc || mode.Platform == PlatformId.ZxSpectrum;
    }

    protected override string Extension
    {
        get { return ".asm"; }
    }

    protected override string CommentPrefix
    {
        get { return ";"; }
    }

    protected override string ByteDirective
    {
        get { return "defb"; }
    }

    protected override string FormatByte(byte value)
    {
        // Το & είναι το πρόθεμα δεκαεξαδικού που χρησιμοποιεί ο κόσμος του Amstrad.
        return "&" + value.ToString("X2", CultureInfo.InvariantCulture);
    }

    protected override void WriteEquates(StringBuilder builder, SpriteExportSource source, int bytesPerRow)
    {
        builder.Append(source.Identifier).Append("_width_bytes equ ").Append(bytesPerRow).AppendLine();
        builder.Append(source.Identifier).Append("_width_px    equ ").Append(source.Width).AppendLine();
        builder.Append(source.Identifier).Append("_height      equ ").Append(source.Height).AppendLine();
        builder.Append(source.Identifier).Append("_frames      equ ").Append(source.Frames.Count).AppendLine();
        builder.AppendLine();
    }
}

/// <summary>6502 σε διάλεκτο ACME — η πιο διαδεδομένη ανοιχτή στη σκηνή του C64.</summary>
public sealed class Acme6502Exporter : SourceExporterBase
{
    public override string FormatId
    {
        get { return "asm-6502"; }
    }

    public override string DisplayName
    {
        get { return "6502 assembly (ACME)"; }
    }

    public override bool Supports(GraphicsMode mode)
    {
        return mode.Platform == PlatformId.Commodore64;
    }

    protected override string Extension
    {
        get { return ".asm"; }
    }

    protected override string CommentPrefix
    {
        get { return ";"; }
    }

    protected override string ByteDirective
    {
        get { return "!byte"; }
    }

    protected override string FormatByte(byte value)
    {
        return "$" + value.ToString("x2", CultureInfo.InvariantCulture);
    }

    protected override void WriteEquates(StringBuilder builder, SpriteExportSource source, int bytesPerRow)
    {
        builder.Append(source.Identifier).Append("_frames = ").Append(source.Frames.Count).AppendLine();

        if (source.Mode.IsHardwareSprite)
        {
            // Ο δείκτης sprite είναι διεύθυνση/64 — η πιο συχνή πηγή σύγχυσης
            // για όποιον στήνει hardware sprites πρώτη φορά.
            builder.AppendLine("; Τα δεδομένα πρέπει να ξεκινούν σε όριο 64 bytes.");
            builder.Append("; Sprite pointer = ").Append(source.Identifier).AppendLine(" / 64");
        }

        builder.AppendLine();
    }
}

/// <summary>Πίνακας C για z88dk, cc65 ή SDCC.</summary>
public sealed class CHeaderExporter : SourceExporterBase
{
    public override string FormatId
    {
        get { return "c"; }
    }

    public override string DisplayName
    {
        get { return "C header (.h)"; }
    }

    public override bool Supports(GraphicsMode mode)
    {
        return true;
    }

    protected override string Extension
    {
        get { return ".h"; }
    }

    protected override string CommentPrefix
    {
        get { return "//"; }
    }

    protected override string ByteDirective
    {
        get { return string.Empty; }
    }

    protected override string FormatByte(byte value)
    {
        return "0x" + value.ToString("X2", CultureInfo.InvariantCulture);
    }

    protected override void WriteEquates(StringBuilder builder, SpriteExportSource source, int bytesPerRow)
    {
        builder.Append("#define ").Append(source.Identifier.ToUpperInvariant())
            .Append("_WIDTH_BYTES ").Append(bytesPerRow).AppendLine();
        builder.Append("#define ").Append(source.Identifier.ToUpperInvariant())
            .Append("_WIDTH_PX ").Append(source.Width).AppendLine();
        builder.Append("#define ").Append(source.Identifier.ToUpperInvariant())
            .Append("_HEIGHT ").Append(source.Height).AppendLine();
        builder.Append("#define ").Append(source.Identifier.ToUpperInvariant())
            .Append("_FRAMES ").Append(source.Frames.Count).AppendLine();
        builder.AppendLine();
    }

    protected override void WriteData(StringBuilder builder, SpriteExportSource source, ExportOptions options)
    {
        var data = SpriteBytes.Pack(source, options);

        WriteEquates(builder, source, SpriteBytes.BytesPerRow(source));

        builder.Append("const unsigned char ").Append(source.Identifier)
            .Append('[').Append(data.Length).AppendLine("] = {");

        var perLine = Math.Max(1, options.BytesPerLine);

        for (var offset = 0; offset < data.Length; offset += perLine)
        {
            var count = Math.Min(perLine, data.Length - offset);

            builder.Append("    ");

            for (var i = 0; i < count; i++)
            {
                builder.Append(FormatByte(data[offset + i]));

                if (offset + i < data.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("};");
    }
}
