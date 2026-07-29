namespace RetroTools.Web.Configuration;

/// <summary>
/// Ρυθμίσεις φιλοξενίας. Δένονται από το section "RetroTools".
/// Καμία από αυτές δεν είναι μυστικό — τα secrets (connection string, OAuth keys)
/// έρχονται πάντα από user-secrets ή environment variables.
/// </summary>
public sealed class RetroToolsOptions
{
    public const string SectionName = "RetroTools";

    /// <summary>
    /// Base path όταν η εφαρμογή σερβίρεται κάτω από sub-path του reverse proxy
    /// (π.χ. "/spritestudio"). Κενό = root.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// Ενεργοποιεί το X-Forwarded-* processing. Πρέπει να είναι true πίσω από reverse proxy,
    /// αλλιώς τα redirect URIs του OAuth θα βγαίνουν http:// αντί για https://.
    /// </summary>
    public bool BehindReverseProxy { get; set; }

    /// <summary>
    /// IP διευθύνσεις των εμπιστευμένων proxies. Αν είναι κενό ΚΑΙ το
    /// <see cref="TrustAnyProxy"/> είναι false, τα forwarded headers αγνοούνται.
    /// </summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>
    /// CIDR δίκτυα εμπιστευμένων proxies, μορφή "10.0.0.0/8".
    /// </summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Δέχεται forwarded headers από οποιαδήποτε πηγή. Χρησιμοποίησέ το ΜΟΝΟ όταν
    /// ο proxy είναι στο ίδιο μηχάνημα/δίκτυο και δεν εκτίθεται η Kestrel απευθείας.
    /// </summary>
    public bool TrustAnyProxy { get; set; }

    /// <summary>
    /// Το HTTPS redirect γίνεται κανονικά στον proxy. Όταν τρέχουμε ως service πίσω από
    /// proxy, το εσωτερικό redirect πρέπει να είναι απενεργοποιημένο.
    /// </summary>
    public bool EnableHttpsRedirection { get; set; } = true;
}
