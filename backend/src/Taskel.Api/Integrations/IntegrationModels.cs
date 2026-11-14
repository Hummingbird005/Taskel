using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Taskel.Api.Directory;
using Taskel.Api.Email;
using Taskel.Api.Keycloak;
using Taskel.Api.Validation;

namespace Taskel.Api.Integrations;

/// <summary>Where an integration's settings come from, and whether they are complete enough to use.</summary>
/// <param name="Saved">True when saved in the interface; false when the server configuration applies.</param>
/// <param name="UpdatedBy">Who saved them last.</param>
/// <param name="SecretSaved">A password or client secret was saved in the interface (it is never sent back).</param>
/// <param name="SecretInConfiguration">The server configuration has one, used when none is saved.</param>
/// <param name="SecretUnreadable">The saved secret can no longer be decrypted and must be entered again.</param>
/// <param name="Ready">The settings are complete: the integration is in use.</param>
public record IntegrationStatus(
    bool Saved, DateTime? UpdatedAt, string? UpdatedBy, bool SecretSaved, bool SecretInConfiguration, bool SecretUnreadable, bool Ready);

// ---- Email ----

/// <param name="Password">Null keeps the password in effect; empty removes the saved one; anything else replaces it.</param>
public record EmailSettingsRequest(
    [StringLength(HostNameAttribute.MaxLength), HostName] string? Host,
    [property: JsonRequired][Range(1, 65535)] int Port,
    [StringLength(256), SafeText] string? Username,
    [StringLength(512)] string? Password,
    [StringLength(StrictEmailAttribute.MaxLength), StrictEmail] string? FromAddress,
    [StringLength(100), SafeText] string? FromName,
    [property: JsonRequired] bool RequireTls,
    [property: JsonRequired] bool NotificationsEnabled,
    [StringLength(2048), HttpUrl] string? PublicUrl)
{
    public EmailSettings ToSettings() => new()
    {
        Smtp = new SmtpOptions
        {
            Host = Trimmed(Host),
            Port = Port,
            Username = Trimmed(Username),
            FromAddress = Trimmed(FromAddress)?.ToLowerInvariant(),
            FromName = Trimmed(FromName),
            RequireTls = RequireTls
        },
        Notifications = new NotificationOptions { Enabled = NotificationsEnabled, PublicUrl = PublicAddress.Parse(PublicUrl) }
    };

    internal static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record EmailSettingsResponse(
    string? Host, int Port, string? Username, string? FromAddress, string? FromName, bool RequireTls,
    bool NotificationsEnabled, string? PublicUrl, IntegrationStatus Status);

/// <summary>Sends a test message, with the settings in the form, to the administrator's own address.</summary>
public record EmailTestRequest([Required] EmailSettingsRequest Settings);

public record EmailTestResponse(string SentTo);

// ---- Active Directory (LDAP) ----

public record DirectorySettingsRequest(
    [property: JsonRequired] bool Enabled,
    [StringLength(HostNameAttribute.MaxLength), HostName] string? Host,
    [property: JsonRequired][Range(1, 65535)] int Port,
    [property: JsonRequired][EnumDataType(typeof(DirectorySecurity))] DirectorySecurity Security,
    [StringLength(HostNameAttribute.MaxLength), HostName] string? Domain,
    [StringLength(1024), SafeText] string? BaseDn,
    [StringLength(1024), SafeText] string? RequiredGroupDn,
    [StringLength(64), Identifier] string? DepartmentAttribute,
    [StringLength(1024), SafeText] string? ManagerGroupDn,
    [StringLength(1024), SafeText] string? AdminGroupDn,
    [property: JsonRequired] bool SyncGroups,
    [StringLength(1024), SafeText] string? GroupsBaseDn,
    [property: JsonRequired][Range(1, 60)] int TimeoutSeconds) : IValidatableObject
{
    public DirectoryOptions ToOptions() => new()
    {
        Enabled = Enabled,
        Host = EmailSettingsRequest.Trimmed(Host),
        Port = Port,
        Security = Security,
        Domain = EmailSettingsRequest.Trimmed(Domain)?.ToLowerInvariant(),
        BaseDn = EmailSettingsRequest.Trimmed(BaseDn),
        RequiredGroupDn = EmailSettingsRequest.Trimmed(RequiredGroupDn),
        DepartmentAttribute = EmailSettingsRequest.Trimmed(DepartmentAttribute) ?? string.Empty,
        ManagerGroupDn = EmailSettingsRequest.Trimmed(ManagerGroupDn),
        AdminGroupDn = EmailSettingsRequest.Trimmed(AdminGroupDn),
        SyncGroups = SyncGroups,
        GroupsBaseDn = EmailSettingsRequest.Trimmed(GroupsBaseDn),
        TimeoutSeconds = TimeoutSeconds
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled) yield break;
        if (string.IsNullOrWhiteSpace(Host)) yield return new ValidationResult("Enter the domain controller.", [nameof(Host)]);
        if (string.IsNullOrWhiteSpace(Domain)) yield return new ValidationResult("Enter the domain name.", [nameof(Domain)]);
        if (string.IsNullOrWhiteSpace(BaseDn)) yield return new ValidationResult("Enter where to look for users.", [nameof(BaseDn)]);
    }
}

public record DirectorySettingsResponse(
    bool Enabled, string? Host, int Port, DirectorySecurity Security, string? Domain, string? BaseDn, string? RequiredGroupDn,
    string? DepartmentAttribute, string? ManagerGroupDn, string? AdminGroupDn, bool SyncGroups, string? GroupsBaseDn,
    int TimeoutSeconds, IntegrationStatus Status);

/// <summary>Signs in with a directory account, using the settings in the form, without creating anything in Taskel.</summary>
public record DirectoryTestRequest(
    [Required] DirectorySettingsRequest Settings,
    [Required, StringLength(128), SafeText] string Username,
    [Required, StringLength(256)] string Password);

/// <param name="SignedIn">False when the name or password is wrong, or the account may not use Taskel.</param>
public record DirectoryTestResponse(
    bool SignedIn, string? Email, string? DisplayName, string? Department, bool? IsManager, bool? IsAdmin,
    IReadOnlyList<string> Groups);

// ---- Keycloak ----

/// <param name="ClientSecret">Null keeps the secret in effect; empty removes the saved one; anything else replaces it.</param>
public record KeycloakSettingsRequest(
    [property: JsonRequired] bool Enabled,
    [StringLength(2048), HttpUrl] string? Authority,
    [StringLength(255), Identifier] string? ClientId,
    [StringLength(512)] string? ClientSecret,
    [Required, StringLength(60, MinimumLength = 1), SafeText] string DisplayName,
    [property: JsonRequired] bool RequireHttpsMetadata,
    [StringLength(255), SafeText] string? RequiredRole,
    [StringLength(255), SafeText] string? AdminRole,
    [StringLength(255), SafeText] string? ManagerRole,
    [StringLength(100), Identifier] string? DepartmentClaim,
    [property: JsonRequired] bool SyncGroups,
    [StringLength(100), Identifier] string? GroupsClaim) : IValidatableObject
{
    public KeycloakOptions ToOptions() => new()
    {
        Enabled = Enabled,
        Authority = EmailSettingsRequest.Trimmed(Authority)?.TrimEnd('/'),
        ClientId = EmailSettingsRequest.Trimmed(ClientId),
        DisplayName = DisplayName.Trim(),
        RequireHttpsMetadata = RequireHttpsMetadata,
        RequiredRole = EmailSettingsRequest.Trimmed(RequiredRole),
        AdminRole = EmailSettingsRequest.Trimmed(AdminRole),
        ManagerRole = EmailSettingsRequest.Trimmed(ManagerRole),
        DepartmentClaim = EmailSettingsRequest.Trimmed(DepartmentClaim),
        SyncGroups = SyncGroups,
        GroupsClaim = EmailSettingsRequest.Trimmed(GroupsClaim) ?? "groups"
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Authority))
        {
            var options = ToOptions();
            if (KeycloakOptions.AuthorityUri(options.Authority, RequireHttpsMetadata) is null)
                yield return new ValidationResult("The realm address must start with https://.", [nameof(Authority)]);
            else if (options.AdminApiBase() is null)
                yield return new ValidationResult("The realm address must end with /realms/<realm name>.", [nameof(Authority)]);
        }
        if (!Enabled) yield break;
        if (string.IsNullOrWhiteSpace(Authority)) yield return new ValidationResult("Enter the realm address.", [nameof(Authority)]);
        if (string.IsNullOrWhiteSpace(ClientId)) yield return new ValidationResult("Enter the client id.", [nameof(ClientId)]);
    }
}

/// <param name="RedirectUri">The address to add to the client's valid redirect URIs in Keycloak.</param>
/// <param name="PostLogoutRedirectUri">The address to add to its valid post logout redirect URIs.</param>
public record KeycloakSettingsResponse(
    bool Enabled, string? Authority, string? ClientId, string DisplayName, bool RequireHttpsMetadata, string? RequiredRole,
    string? AdminRole, string? ManagerRole, string? DepartmentClaim, bool SyncGroups, string GroupsClaim,
    string RedirectUri, string PostLogoutRedirectUri, IntegrationStatus Status);

public record KeycloakTestRequest([Required] KeycloakSettingsRequest Settings);

/// <param name="Skipped">Accounts that could not be brought in, with the reason, at most 50.</param>
public record KeycloakImportResponse(int Total, int Created, int Updated, int SkippedCount, IReadOnlyList<KeycloakImportSkip> Skipped);

public record KeycloakImportSkip(string Who, KeycloakRefusal Reason);
