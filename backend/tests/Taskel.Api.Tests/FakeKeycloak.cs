using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Taskel.Api.Tests;

/// <summary>
/// Stands in for a Keycloak realm over HTTP: discovery, signing keys, the token endpoint (for the
/// sign-in code and the service account) and the admin API's user list.
/// </summary>
public class FakeKeycloak : HttpMessageHandler
{
    public const string Authority = "https://keycloak.test/realms/taskel";
    public const string ClientId = "taskel";
    public const string ClientSecret = "fake-client-secret";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly ConcurrentDictionary<string, (string Nonce, Dictionary<string, object> Claims, Dictionary<string, object> AccessClaims)> _codes = new();

    /// <summary>What the admin API lists, as Keycloak's user representations.</summary>
    public List<object> Users { get; } = [];

    /// <summary>Whether the client secret is accepted for the client credentials grant.</summary>
    public bool ServiceAccountEnabled { get; set; } = true;

    /// <summary>Whether the service account may list users.</summary>
    public bool CanListUsers { get; set; } = true;

    public int RequestCount;

    /// <summary>
    /// Registers what Keycloak would say about the person signing in, and returns the code the
    /// browser would bring back to Taskel.
    /// </summary>
    /// <param name="idClaims">ID token claims besides iss, aud, exp, iat and nonce.</param>
    /// <param name="accessClaims">Access token claims, e.g. realm_access.</param>
    public string Authorize(string nonce, Dictionary<string, object> idClaims, Dictionary<string, object>? accessClaims = null)
    {
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = (nonce, idClaims, accessClaims ?? []);
        return code;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref RequestCount);
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host != "keycloak.test") return new HttpResponseMessage(HttpStatusCode.NotFound);

        return path switch
        {
            "/realms/taskel/.well-known/openid-configuration" => Json(new Dictionary<string, object>
            {
                ["issuer"] = Authority,
                ["authorization_endpoint"] = $"{Authority}/protocol/openid-connect/auth",
                ["token_endpoint"] = $"{Authority}/protocol/openid-connect/token",
                ["jwks_uri"] = $"{Authority}/protocol/openid-connect/certs",
                ["end_session_endpoint"] = $"{Authority}/protocol/openid-connect/logout",
                ["response_types_supported"] = new[] { "code" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" }
            }),
            "/realms/taskel/protocol/openid-connect/certs" => Json(new { keys = new[] { Jwk() } }),
            "/realms/taskel/protocol/openid-connect/token" => await TokenAsync(request, cancellationToken),
            "/admin/realms/taskel/users" => ListUsers(request),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var form = (await request.Content!.ReadAsStringAsync(ct))
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : ""));

        if (form.GetValueOrDefault("grant_type") == "client_credentials")
        {
            return ServiceAccountEnabled && form.GetValueOrDefault("client_secret") == ClientSecret
                ? Json(new { access_token = "service-account-token", token_type = "Bearer" })
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (form.GetValueOrDefault("grant_type") != "authorization_code"
            || !_codes.TryRemove(form.GetValueOrDefault("code") ?? "", out var granted))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var idClaims = new Dictionary<string, object>(granted.Claims) { ["nonce"] = granted.Nonce };
        return Json(new
        {
            access_token = Sign(granted.AccessClaims),
            id_token = Sign(idClaims),
            token_type = "Bearer",
            expires_in = 300
        });
    }

    private HttpResponseMessage ListUsers(HttpRequestMessage request)
    {
        if (request.Headers.Authorization?.Parameter != "service-account-token" || !CanListUsers)
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var first = int.Parse(query["first"] ?? "0");
        var max = int.Parse(query["max"] ?? "100");
        return Json(Users.Skip(first).Take(max).ToList());
    }

    private string Sign(Dictionary<string, object> claims)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now.AddSeconds(-5),
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256)
        });
    }

    private object Jwk()
    {
        var parameters = _key.Rsa.ExportParameters(false);
        return new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid = _key.KeyId,
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
}
