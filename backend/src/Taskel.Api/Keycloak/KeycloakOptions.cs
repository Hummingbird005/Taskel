using System.Text.Json.Serialization;

namespace Taskel.Api.Keycloak;

/// <summary>
/// Sign-in with Keycloak (OpenID Connect). Bound from the <c>Keycloak</c> configuration section, and
/// replaced by what an administrator saves in the interface.
/// </summary>
public class KeycloakOptions
{
    public const string Section = "Keycloak";
    /// <summary>The authentication scheme name, and the route segment of the sign-in endpoints.</summary>
    public const string Scheme = "Keycloak";
    public const string CallbackPath = "/api/auth/keycloak/callback";

    public bool Enabled { get; set; }
    /// <summary>The realm's address, e.g. <c>https://sso.example.com/realms/company</c>.</summary>
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Empty for a public client. Set through an environment variable or the interface, never in appsettings.json.</summary>
    public string? ClientSecret { get; set; }
    /// <summary>The text on the sign-in button.</summary>
    public string DisplayName { get; set; } = "Keycloak";
    /// <summary>Only for a test Keycloak served over plain HTTP. Never turn off for a real one.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
    /// <summary>When set, only users with this realm or client role may sign in.</summary>
    public string? RequiredRole { get; set; }
    /// <summary>When set, users with this realm or client role are administrators in Taskel, and others are not.</summary>
    public string? AdminRole { get; set; }
    /// <summary>When set, users with this realm or client role are managers in Taskel, and others are not.</summary>
    public string? ManagerRole { get; set; }
    /// <summary>The token claim (or, for the user import, the user attribute) holding the department.</summary>
    public string? DepartmentClaim { get; set; } = "department";
    /// <summary>Whether the groups in <see cref="GroupsClaim"/> become groups in Taskel that tasks can be given to.</summary>
    public bool SyncGroups { get; set; }
    /// <summary>The token claim listing the user's groups (Keycloak's "Group Membership" mapper).</summary>
    public string GroupsClaim { get; set; } = "groups";

    [JsonIgnore]
    public bool IsUsable =>
        Enabled && !string.IsNullOrWhiteSpace(ClientId) && AuthorityUri(Authority, RequireHttpsMetadata) is not null;

    /// <summary>The authority as an absolute http(s) address, or null when it is not one.</summary>
    public static Uri? AuthorityUri(string? authority, bool requireHttps) =>
        Uri.TryCreate(authority?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (!requireHttps && uri.Scheme == Uri.UriSchemeHttp))
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;

    /// <summary>
    /// The realm's admin REST address, worked out from the authority:
    /// <c>https://host/realms/company</c> becomes <c>https://host/admin/realms/company</c>.
    /// </summary>
    public Uri? AdminApiBase()
    {
        if (AuthorityUri(Authority, RequireHttpsMetadata) is not { } authority) return null;
        var path = authority.AbsolutePath.TrimEnd('/');
        var at = path.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (at < 0) return null;
        var realm = path[(at + "/realms/".Length)..];
        if (realm.Length == 0 || realm.Contains('/')) return null;
        return new Uri(authority, $"{path[..at]}/admin/realms/{realm}/");
    }
}
