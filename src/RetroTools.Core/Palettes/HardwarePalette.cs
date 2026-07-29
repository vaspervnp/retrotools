namespace RetroTools.Core.Palettes;

/// <summary>
/// Η πλήρης, αμετάβλητη παλέτα υλικού μιας πλατφόρμας: ποια χρώματα υπάρχουν,
/// πώς ονομάζονται, τι στέλνεται στο υλικό και πώς φαίνονται σε κάθε profile.
/// </summary>
public sealed class HardwarePalette
{
    private readonly Dictionary<string, PaletteProfile> _profilesById;
    private readonly Dictionary<byte, int> _hardwareValueToIndex;

    public HardwarePalette(
        string platformCode,
        IReadOnlyList<HardwareColor> colors,
        IReadOnlyList<PaletteProfile> profiles,
        string defaultProfileId)
    {
        if (colors == null || colors.Count == 0)
        {
            throw new ArgumentException("Η παλέτα πρέπει να έχει τουλάχιστον ένα χρώμα.", nameof(colors));
        }

        if (profiles == null || profiles.Count == 0)
        {
            throw new ArgumentException("Η παλέτα πρέπει να έχει τουλάχιστον ένα profile.", nameof(profiles));
        }

        foreach (var profile in profiles)
        {
            if (profile.Colors.Count != colors.Count)
            {
                throw new ArgumentException(
                    "Το profile '" + profile.Id + "' έχει " + profile.Colors.Count +
                    " χρώματα ενώ η παλέτα έχει " + colors.Count + ".",
                    nameof(profiles));
            }
        }

        PlatformCode = platformCode;
        Colors = colors;
        Profiles = profiles;

        _profilesById = profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        if (!_profilesById.TryGetValue(defaultProfileId, out var defaultProfile))
        {
            throw new ArgumentException("Άγνωστο default profile: " + defaultProfileId, nameof(defaultProfileId));
        }

        DefaultProfile = defaultProfile;

        // Αντίστροφη αναζήτηση για import: hardware τιμή → δείκτης χρώματος.
        // Στον CPC πολλά hardware inks δείχνουν στο ίδιο firmware χρώμα.
        _hardwareValueToIndex = new Dictionary<byte, int>();
        foreach (var color in colors)
        {
            foreach (var value in color.HardwareValues)
            {
                _hardwareValueToIndex[value] = color.Index;
            }
        }
    }

    public string PlatformCode { get; }

    public IReadOnlyList<HardwareColor> Colors { get; }

    public IReadOnlyList<PaletteProfile> Profiles { get; }

    public PaletteProfile DefaultProfile { get; }

    public int Count
    {
        get { return Colors.Count; }
    }

    public HardwareColor this[int index]
    {
        get { return Colors[index]; }
    }

    public PaletteProfile GetProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return DefaultProfile;
        }

        return _profilesById.TryGetValue(profileId, out var profile) ? profile : DefaultProfile;
    }

    public Rgb24 GetRgb(int colorIndex, string? profileId = null)
    {
        return GetProfile(profileId)[colorIndex];
    }

    /// <summary>
    /// Μετατρέπει τιμή υλικού (π.χ. CPC Gate Array ink 0x4B) σε δείκτη χρώματος.
    /// </summary>
    public bool TryGetIndexByHardwareValue(byte hardwareValue, out int colorIndex)
    {
        return _hardwareValueToIndex.TryGetValue(hardwareValue, out colorIndex);
    }

    /// <summary>
    /// Βρίσκει το πλησιέστερο χρώμα της παλέτας σε γραμμικό RGB — για import από PNG.
    /// </summary>
    public int FindNearest(Rgb24 color, string? profileId = null)
    {
        var profile = GetProfile(profileId);
        var bestIndex = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < profile.Colors.Count; i++)
        {
            var distance = color.LinearDistanceSquaredTo(profile.Colors[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Τα οπτικά μοναδικά χρώματα. Χρήσιμο για το ZX Spectrum, όπου το
    /// "bright black" είναι το ίδιο μαύρο με το κανονικό (16 δείκτες → 15 χρώματα).
    /// </summary>
    public int CountDistinctColors(string? profileId = null)
    {
        return GetProfile(profileId).Colors.Distinct().Count();
    }
}
