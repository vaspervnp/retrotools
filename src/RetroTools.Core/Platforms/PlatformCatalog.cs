using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Platforms;

/// <summary>
/// Η μοναδική πηγή αλήθειας για τα χαρακτηριστικά του υλικού.
/// Ο πίνακας <c>platforms</c> / <c>platform_modes</c> της βάσης γεμίζει με seed
/// από εδώ — ποτέ το αντίστροφο.
/// </summary>
public static class PlatformCatalog
{
    private static readonly IReadOnlyList<PlatformDefinition> Platforms = new[]
    {
        AmstradCpcPlatform.Create(),
        Commodore64Platform.Create(),
        ZxSpectrumPlatform.Create(),
    };

    private static readonly Dictionary<string, PlatformDefinition> ByCode =
        Platforms.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<PlatformId, PlatformDefinition> ById =
        Platforms.ToDictionary(p => p.Id);

    private static readonly Dictionary<string, GraphicsMode> ModesByCode =
        Platforms.SelectMany(p => p.Modes).ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PlatformDefinition> All
    {
        get { return Platforms; }
    }

    public static IReadOnlyList<GraphicsMode> AllModes
    {
        get { return ModesByCode.Values.ToList(); }
    }

    public static PlatformDefinition Get(PlatformId id)
    {
        if (!ById.TryGetValue(id, out var platform))
        {
            throw new KeyNotFoundException("Άγνωστη πλατφόρμα: " + id);
        }

        return platform;
    }

    public static PlatformDefinition Get(string code)
    {
        if (!ByCode.TryGetValue(code, out var platform))
        {
            throw new KeyNotFoundException(
                "Άγνωστος κωδικός πλατφόρμας '" + code + "'. Διαθέσιμοι: " + string.Join(", ", ByCode.Keys));
        }

        return platform;
    }

    public static bool TryGet(string code, out PlatformDefinition? platform)
    {
        return ByCode.TryGetValue(code, out platform);
    }

    /// <summary>Βρίσκει mode από τον πλήρη κωδικό του, π.χ. "cpc.mode0".</summary>
    public static GraphicsMode GetMode(string modeCode)
    {
        if (!ModesByCode.TryGetValue(modeCode, out var mode))
        {
            throw new KeyNotFoundException(
                "Άγνωστος κωδικός mode '" + modeCode + "'. Διαθέσιμοι: " + string.Join(", ", ModesByCode.Keys));
        }

        return mode;
    }

    public static bool TryGetMode(string modeCode, out GraphicsMode? mode)
    {
        return ModesByCode.TryGetValue(modeCode, out mode);
    }
}
