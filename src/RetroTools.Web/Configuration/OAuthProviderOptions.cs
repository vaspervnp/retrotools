namespace RetroTools.Web.Configuration;

/// <summary>
/// Κλειδιά ενός OAuth provider. Έρχονται <b>μόνο</b> από user-secrets ή environment
/// variables — ποτέ από committed αρχείο.
/// </summary>
public sealed class OAuthProviderOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Ο provider καταχωρείται μόνο αν έχει και τα δύο κλειδιά. Έτσι η εφαρμογή
    /// σηκώνεται κανονικά σε περιβάλλον χωρίς ρυθμισμένο OAuth.
    /// </summary>
    public bool IsConfigured
    {
        get { return !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret); }
    }
}

public sealed class AuthenticationSettings
{
    public const string SectionName = "Authentication";

    public OAuthProviderOptions GitHub { get; set; } = new OAuthProviderOptions();

    public OAuthProviderOptions Google { get; set; } = new OAuthProviderOptions();
}
