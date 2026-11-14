using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Taskel.Api.Validation;

namespace Taskel.Api.Keycloak;

/// <summary>A Keycloak account, as read from its tokens at sign-in or from the admin API at import.</summary>
/// <param name="Subject">The account's id in Keycloak (<c>sub</c>): stable across renames and email changes.</param>
/// <param name="EmailVerified">Whether Keycloak verified the address. An existing Taskel account is only linked on a verified address.</param>
/// <param name="Roles">Realm roles and this client's roles; null when they are not known (the import).</param>
/// <param name="Groups">Group paths, e.g. <c>/Sales/Offers</c>; null when they are not known or not brought in.</param>
public record KeycloakUser(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName,
    string? Department,
    IReadOnlySet<string>? Roles,
    IReadOnlyList<string>? Groups);

public static class KeycloakClaims
{
    public const int MaxGroups = 200;

    /// <summary>
    /// Reads the account from the validated ID token's claims and the access token. Keycloak puts
    /// roles only in the access token by default (<c>realm_access</c>, <c>resource_access</c>); it was
    /// received straight from Keycloak's token endpoint over the back channel, so its content is
    /// trusted without checking its signature again.
    /// </summary>
    public static KeycloakUser? Read(ClaimsPrincipal idToken, string? accessToken, KeycloakOptions options)
    {
        var access = Payload(accessToken);
        var subject = idToken.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255) return null;

        string? Claim(string type) => idToken.FindFirst(type)?.Value ?? StringValue(access, type);

        var email = Claim("email")?.Trim().ToLowerInvariant();
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var name = Claim("name");
        if (string.IsNullOrWhiteSpace(name))
            name = string.Join(' ', new[] { Claim("given_name"), Claim("family_name") }.Where(n => !string.IsNullOrWhiteSpace(n)));
        if (string.IsNullOrWhiteSpace(name)) name = Claim("preferred_username");

        var department = string.IsNullOrWhiteSpace(options.DepartmentClaim) ? null : Claim(options.DepartmentClaim.Trim());

        var roles = new HashSet<string>(StringComparer.Ordinal);
        roles.UnionWith(idToken.FindAll("roles").Select(c => c.Value));
        roles.UnionWith(Strings(access, "realm_access", "roles"));
        if (!string.IsNullOrWhiteSpace(options.ClientId))
            roles.UnionWith(Strings(access, "resource_access", options.ClientId.Trim(), "roles"));

        List<string>? groups = null;
        if (options.SyncGroups && !string.IsNullOrWhiteSpace(options.GroupsClaim))
        {
            var claim = options.GroupsClaim.Trim();
            groups = idToken.FindAll(claim).Select(c => c.Value).ToList();
            if (groups.Count == 0) groups = Strings(access, claim);
        }

        return Create(subject, email, verified, name, department, roles, groups);
    }

    /// <summary>Builds the account with every value cleaned: unsafe characters out, lengths capped, an invalid email dropped.</summary>
    public static KeycloakUser Create(
        string subject, string? email, bool emailVerified, string? displayName, string? department,
        IReadOnlySet<string>? roles, IReadOnlyList<string>? groups)
    {
        var cleanEmail = StrictEmailAttribute.IsValidAddress(email) ? email : null;
        return new KeycloakUser(
            subject,
            cleanEmail,
            emailVerified && cleanEmail is not null,
            Clean(displayName, 256),
            Clean(department, Models.User.MaxDepartmentLength),
            roles,
            groups?.Select(g => Clean(g, 900)).OfType<string>().Distinct(StringComparer.Ordinal).Take(MaxGroups).ToList());
    }

    /// <summary>The Taskel group for a Keycloak group path: <c>/Sales/Offers</c> is named "Offers".</summary>
    public static Integrations.ExternalGroup Group(string path)
    {
        var trimmed = path.Trim();
        var name = trimmed.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        if (name.Length == 0) name = trimmed;
        if (name.Length > Models.Group.MaxNameLength) name = name[..Models.Group.MaxNameLength];
        return new Integrations.ExternalGroup("keycloak:" + trimmed.ToLowerInvariant(), name);
    }

    private static string? Clean(string? value, int maxLength)
    {
        var clean = SafeText.Clean(value, multiline: false).Trim();
        return clean.Length is > 0 && clean.Length <= maxLength ? clean : null;
    }

    /// <summary>The payload of a JWT, or null when the token is not one (an opaque token, or none).</summary>
    private static JsonElement? Payload(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonElement? Walk(JsonElement? root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var next)) return null;
            current = next;
        }
        return current;
    }

    private static string? StringValue(JsonElement? root, string name) => Walk(root, name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        _ => null
    };

    private static List<string> Strings(JsonElement? root, params string[] path) => Walk(root, path) switch
    {
        { ValueKind: JsonValueKind.Array } array => array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList(),
        { ValueKind: JsonValueKind.String } single => [single.GetString()!],
        _ => []
    };
}
