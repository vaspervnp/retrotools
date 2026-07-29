using RetroTools.Core.Codecs;
using RetroTools.Core.Platforms;

namespace RetroTools.Core.Export;

/// <summary>
/// Κοινή λογική συλλογής bytes: όλοι οι exporters παράγουν τα ίδια δεδομένα και
/// διαφέρουν μόνο στο περιτύλιγμα.
/// </summary>
public static class SpriteBytes
{
    /// <summary>
    /// Τα packed bytes όλων των καρέ, το ένα μετά το άλλο, γραμμή-γραμμή.
    /// </summary>
    public static byte[] Pack(SpriteExportSource source, ExportOptions options)
    {
        var codec = SpriteCodecs.For(source.Mode);
        var blocks = new List<byte[]>();

        for (var i = 0; i < source.Frames.Count; i++)
        {
            var frame = source.Frames[i];
            var hasMask = options.IncludeMask && i < source.Masks.Count;

            if (hasMask)
            {
                // Τα δεδομένα μηδενίζονται εκεί που το sprite είναι διαφανές, ώστε
                // το OR της ρουτίνας σχεδίασης να μη «λερώνει» το φόντο.
                blocks.Add(codec.Pack(MaskCodec.ApplyMask(frame, source.Masks[i])));
                blocks.Add(MaskCodec.PackAndMask(source.Masks[i]));
            }
            else
            {
                blocks.Add(codec.Pack(frame));
            }
        }

        var total = blocks.Sum(b => b.Length);
        var result = new byte[total];
        var position = 0;

        foreach (var block in blocks)
        {
            Array.Copy(block, 0, result, position, block.Length);
            position += block.Length;
        }

        return result;
    }

    public static int BytesPerRow(SpriteExportSource source)
    {
        return SpriteCodecs.For(source.Mode).BytesPerRow(source.Width);
    }
}

/// <summary>Ωμά bytes, ακριβώς όπως θα κάθονταν στη μνήμη.</summary>
public sealed class BinaryExporter : ISpriteExporter
{
    public string FormatId
    {
        get { return "bin"; }
    }

    public string DisplayName
    {
        get { return "Raw binary (.bin)"; }
    }

    public bool Supports(GraphicsMode mode)
    {
        return true;
    }

    public ExportResult Export(SpriteExportSource source, ExportOptions options)
    {
        return new ExportResult(
            source.Identifier + ".bin",
            "application/octet-stream",
            SpriteBytes.Pack(source, options));
    }
}

/// <summary>
/// Δυαδικό C64 με 2-byte διεύθυνση φόρτωσης μπροστά — η μορφή που φορτώνει
/// κατευθείαν στον VICE (drag &amp; drop ή <c>LOAD"*",8,1</c>).
/// </summary>
public sealed class PrgExporter : ISpriteExporter
{
    public string FormatId
    {
        get { return "prg"; }
    }

    public string DisplayName
    {
        get { return "C64 program (.prg) — φορτώνει σε VICE"; }
    }

    public bool Supports(GraphicsMode mode)
    {
        return mode.Platform == PlatformId.Commodore64;
    }

    public ExportResult Export(SpriteExportSource source, ExportOptions options)
    {
        if (!Supports(source.Mode))
        {
            throw new InvalidOperationException("Η μορφή .prg αφορά μόνο τον Commodore 64.");
        }

        var data = SpriteBytes.Pack(source, options);
        var content = new byte[data.Length + 2];

        // Little-endian, όπως το περιμένει η ρουτίνα φόρτωσης του KERNAL.
        content[0] = (byte)(options.LoadAddress & 0xFF);
        content[1] = (byte)((options.LoadAddress >> 8) & 0xFF);

        Array.Copy(data, 0, content, 2, data.Length);

        return new ExportResult(source.Identifier + ".prg", "application/octet-stream", content);
    }
}
