using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskel.Api.Keycloak;

public class KeycloakUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What the "test" button learned about a Keycloak realm.</summary>
/// <param name="Issuer">The realm's issuer, as its discovery document gives it.</param>
/// <param name="ServiceAccount">
/// Whether the client secret works for the client credentials grant (needed for the user import).
/// Null when there is no secret to try.
/// </param>
/// <param name="CanListUsers">Whether that service account may read the realm's users; null when not tried.</param>
public record KeycloakCheck(string Issuer, bool HasEndSession, bool? ServiceAccount, bool? CanListUsers, string? Problem);

/// <summary>
/// Talks to Keycloak outside of the sign-in itself: its discovery document, and the admin REST API
/// for importing users. The import uses the client's own service account, which needs the
/// <c>view-users</c> role of the <c>realm-management</c> client.
/// </summary>
public class KeycloakAdminClient(HttpClient http, ILogger<KeycloakAdminClient> logger)
{
    public const string HttpClientName = "keycloak";
    /// <summary>The import stops after this many users.</summary>
    public const int MaxUsers = 10_000;
    private const int PageSize = 100;
    private const long MaxResponseBytes = 4 * 1024 * 1024;

    private sealed record Discovery(
        [property: JsonPropertyName("issuer")] string? Issuer,
        [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint,
        [property: JsonPropertyName("end_session_endpoint")] string? EndSessionEndpoint);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("emailVerified")] bool EmailVerified,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("attributes")] Dictionary<string, List<string>>? Attributes);

    /// <summary>Reads the discovery document and, when there is a client secret, tries the service account.</summary>
    /// <exception cref="KeycloakUnavailableException">Keycloak could not be reached or answered something unexpected.</exception>
    public async Task<KeycloakCheck> CheckAsync(KeycloakOptions options, CancellationToken ct)
    {
        var discovery = await DiscoverAsync(options, ct);
        if (string.IsNullOrEmpty(options.ClientSecret))
            return new KeycloakCheck(discovery.Issuer!, discovery.EndSessionEndpoint is not null, null, null, null);

        string token;
        try
        {
            token = await ServiceTokenAsync(options, discovery, ct);
        }
        catch (KeycloakUnavailableException ex)
        {
            return new KeycloakCheck(discovery.Issuer!, discovery.EndSessionEndpoint is not null, false, null, ex.Message);
        }

        var users = new Uri(options.AdminApiBase()!, "users?first=0&max=1&briefRepresentation=true");
        using var request = new HttpRequestMessage(HttpMethod.Get, users);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, ct);
        var canList = response.IsSuccessStatusCode;
        return new KeycloakCheck(discovery.Issuer!, discovery.EndSessionEndpoint is not null, true, canList,
            canList ? null : $"Reading the users was refused ({(int)response.StatusCode}). Give the client's service account the view-users role.");
    }

    /// <summary>Reads every enabled user of the realm, up to <see cref="MaxUsers"/>.</summary>
    /// <exception cref="KeycloakUnavailableException">Keycloak could not be reached, or refused the service account.</exception>
    public async Task<IReadOnlyList<KeycloakUser>> ListUsersAsync(KeycloakOptions options, CancellationToken ct)
    {
        var discovery = await DiscoverAsync(options, ct);
        if (string.IsNullOrEmpty(options.ClientSecret))
            throw new KeycloakUnavailableException("Importing users needs a confidential client with a client secret.");
        var token = await ServiceTokenAsync(options, discovery, ct);

        var result = new List<KeycloakUser>();
        for (var first = 0; first < MaxUsers; first += PageSize)
        {
            var page = new Uri(options.AdminApiBase()!, $"users?first={first}&max={PageSize}&briefRepresentation=false");
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new KeycloakUnavailableException(
                    $"Reading the users was refused ({(int)response.StatusCode}). Give the client's service account the view-users role.");
            }
            var users = await ReadJsonAsync<List<UserRepresentation>>(response, ct) ?? [];
            result.AddRange(users.Where(u => u.Enabled && !string.IsNullOrWhiteSpace(u.Id)).Select(u => ToUser(u, options)));
            if (users.Count < PageSize) break;
        }
        return result;
    }

    private static KeycloakUser ToUser(UserRepresentation user, KeycloakOptions options)
    {
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        if (string.IsNullOrWhiteSpace(name)) name = user.Username ?? string.Empty;
        string? department = null;
        if (!string.IsNullOrWhiteSpace(options.DepartmentClaim)
            && user.Attributes?.TryGetValue(options.DepartmentClaim.Trim(), out var values) == true)
        {
            department = values.FirstOrDefault();
        }
        return KeycloakClaims.Create(user.Id!, user.Email?.Trim().ToLowerInvariant(), user.EmailVerified, name, department,
            roles: null, groups: null);
    }

    private async Task<Discovery> DiscoverAsync(KeycloakOptions options, CancellationToken ct)
    {
        if (KeycloakOptions.AuthorityUri(options.Authority, options.RequireHttpsMetadata) is not { } authority)
            throw new KeycloakUnavailableException("The realm address is not a valid https address.");
        if (options.AdminApiBase() is null)
            throw new KeycloakUnavailableException("The realm address must end with /realms/<realm name>.");

        var address = new Uri(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new KeycloakUnavailableException($"The realm's discovery document was not found ({(int)response.StatusCode}). Check the realm address.");
        var discovery = await ReadJsonAsync<Discovery>(response, ct);
        if (discovery?.Issuer is null || discovery.TokenEndpoint is null)
            throw new KeycloakUnavailableException("The address does not serve an OpenID Connect discovery document.");
        if (!string.Equals(discovery.Issuer.TrimEnd('/'), authority.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new KeycloakUnavailableException(
                $"Keycloak names its realm {discovery.Issuer}, which differs from the address entered. Use exactly that address.");
        }
        return discovery;
    }

    private async Task<string> ServiceTokenAsync(KeycloakOptions options, Discovery discovery, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId?.Trim() ?? string.Empty,
                ["client_secret"] = options.ClientSecret ?? string.Empty
            })
        };
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakUnavailableException(
                $"Keycloak refused the client id and secret ({(int)response.StatusCode}). Check them, and that the client has service accounts enabled.");
        }
        var token = await ReadJsonAsync<TokenResponse>(response, ct);
        return token?.AccessToken ?? throw new KeycloakUnavailableException("Keycloak returned no access token.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Keycloak request to {Host} failed", request.RequestUri?.Host);
            throw new KeycloakUnavailableException($"Keycloak could not be reached: {ex.Message}", ex);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new KeycloakUnavailableException("Keycloak's answer is too large.");
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (limited.Length + read > MaxResponseBytes) throw new KeycloakUnavailableException("Keycloak's answer is too large.");
                limited.Write(buffer, 0, read);
            }
            limited.Position = 0;
            return await JsonSerializer.DeserializeAsync<T>(limited, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new KeycloakUnavailableException("Keycloak's answer could not be read.", ex);
        }
    }
}
