namespace RetroTools.Core.Platforms;

public enum PlatformId
{
    AmstradCpc = 1,
    Commodore64 = 2,
    ZxSpectrum = 3,
}

/// <summary>Οικογένεια επεξεργαστή — καθορίζει τον προεπιλεγμένο assembler exporter.</summary>
public enum CpuFamily
{
    Z80 = 1,
    Mos6502 = 2,
}

/// <summary>
/// Πού «ζει» το χρώμα σε κάθε mode — ο πιο σημαντικός περιορισμός για τον editor.
/// </summary>
public enum ColorScope
{
    /// <summary>Κάθε pixel διαλέγει ελεύθερα από την παλέτα (CPC).</summary>
    PerPixel = 0,

    /// <summary>Τα χρώματα ορίζονται ανά κελί 8×8 (ZX attribute clash, C64 bitmap/char).</summary>
    PerCell = 1,

    /// <summary>Τα χρώματα ορίζονται ανά sprite (C64 hardware sprites).</summary>
    PerSprite = 2,
}
