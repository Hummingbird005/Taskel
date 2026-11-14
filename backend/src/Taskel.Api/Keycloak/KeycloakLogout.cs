using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Taskel.Api.Integrations;

namespace Taskel.Api.Keycloak;

/// <summary>The address that ends a Keycloak session and brings the browser back to Taskel.</summary>
public class KeycloakLogout(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions, IntegrationSettings settings, ILogger<KeycloakLogout> logger)
{
    /// <summary>Null when Keycloak offers no sign-out address or cannot be reached; the Taskel session ends anyway.</summary>
    public async Task<string?> AddressAsync(HttpRequest request, string? idToken, CancellationToken ct)
    {
        try
        {
            var configuration = await oidcOptions.Get(KeycloakOptions.Scheme).ConfigurationManager!.GetConfigurationAsync(ct);
            if (string.IsNullOrEmpty(configuration.EndSessionEndpoint)) return null;
            var query = new Dictionary<string, string?>
            {
                ["client_id"] = settings.Keycloak.ClientId,
                ["post_logout_redirect_uri"] = PublicAddress.HomeOf(request, settings)
            };
            if (!string.IsNullOrEmpty(idToken)) query["id_token_hint"] = idToken;
            return QueryHelpers.AddQueryString(configuration.EndSessionEndpoint, query);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Keycloak's sign-out address could not be read");
            return null;
        }
    }
}
