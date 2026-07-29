using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RetroTools.Web.Configuration;

/// <summary>
/// Στήσιμο για self-hosted λειτουργία ως service (Windows Service / systemd),
/// πίσω από reverse proxy (nginx / Apache / IIS ARR / Caddy).
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Προσθέτει τα appsettings.Local.json (gitignored) στην αλυσίδα ρυθμίσεων,
    /// με προτεραιότητα πάνω από τα committed appsettings.
    /// </summary>
    public static void AddLocalConfiguration(this ConfigurationManager configuration, IHostEnvironment environment)
    {
        configuration
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings." + environment.EnvironmentName + ".Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    }

    public static void ConfigureForwardedHeaders(this IServiceCollection services, RetroToolsOptions options)
    {
        if (!options.BehindReverseProxy)
        {
            return;
        }

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            // Χωρίς αυτό, το ASP.NET Core δέχεται μόνο proxy στο loopback.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();

            if (options.TrustAnyProxy)
            {
                // Δέχεται τα headers από οποιαδήποτε πηγή — μόνο αν η Kestrel δεν εκτίθεται.
                return;
            }

            foreach (var proxy in options.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    forwarded.KnownProxies.Add(address);
                }
            }

            foreach (var network in options.KnownNetworks)
            {
                // Δέχεται μορφή CIDR, π.χ. "10.0.0.0/8". Λάθος τιμές αγνοούνται σιωπηλά
                // αντί να ρίξουν την εφαρμογή στο startup.
                if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
                {
                    forwarded.KnownIPNetworks.Add(ipNetwork);
                }
            }
        });
    }
}
