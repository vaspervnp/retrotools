using RetroTools.Core.Palettes;

namespace RetroTools.Core.Platforms;

/// <summary>
/// Μια υποστηριζόμενη πλατφόρμα με την παλέτα και τα modes της.
/// </summary>
public sealed class PlatformDefinition
{
    private readonly Dictionary<string, GraphicsMode> _modesByCode;

    public PlatformDefinition(
        PlatformId id,
        string code,
        string name,
        string manufacturer,
        int year,
        CpuFamily cpu,
        HardwarePalette palette,
        IReadOnlyList<GraphicsMode> modes,
        bool hasHardwareSprites,
        bool hasProgrammablePalette)
    {
        Id = id;
        Code = code;
        Name = name;
        Manufacturer = manufacturer;
        Year = year;
        Cpu = cpu;
        Palette = palette;
        Modes = modes;
        HasHardwareSprites = hasHardwareSprites;
        HasProgrammablePalette = hasProgrammablePalette;

        _modesByCode = modes.ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);
    }

    public PlatformId Id { get; }

    /// <summary>Σταθερός κωδικός για βάση και URLs: "cpc", "c64", "zx".</summary>
    public string Code { get; }

    public string Name { get; }

    public string Manufacturer { get; }

    public int Year { get; }

    public CpuFamily Cpu { get; }

    public HardwarePalette Palette { get; }

    public IReadOnlyList<GraphicsMode> Modes { get; }

    /// <summary>Αληθές μόνο για τον C64 από τις τρεις υποστηριζόμενες πλατφόρμες.</summary>
    public bool HasHardwareSprites { get; }

    /// <summary>Αληθές μόνο για τον CPC: τα 16 pens δείχνουν σε οποιαδήποτε από τα 27 χρώματα.</summary>
    public bool HasProgrammablePalette { get; }

    public GraphicsMode DefaultMode
    {
        get { return Modes[0]; }
    }

    public GraphicsMode GetMode(string modeCode)
    {
        if (!_modesByCode.TryGetValue(modeCode, out var mode))
        {
            throw new KeyNotFoundException(
                "Άγνωστο mode '" + modeCode + "' για την πλατφόρμα " + Code + ". Διαθέσιμα: " +
                string.Join(", ", _modesByCode.Keys));
        }

        return mode;
    }

    public bool TryGetMode(string modeCode, out GraphicsMode? mode)
    {
        return _modesByCode.TryGetValue(modeCode, out mode);
    }
}
