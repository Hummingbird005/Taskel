using Taskel.Api.Models;

namespace Taskel.Api.Directory;

public enum DirectorySecurity
{
    /// <summary>LDAP over TLS from the start (port 636). The default.</summary>
    Ldaps,
    /// <summary>Plain connection upgraded with StartTLS (port 389).</summary>
    StartTls,
    /// <summary>No encryption: passwords cross the network in clear text. Test environments only.</summary>
    None
}

/// <summary>Bound from the <c>ActiveDirectory</c> configuration section.</summary>
public class DirectoryOptions
{
    public const string Section = "ActiveDirectory";

    public bool Enabled { get; set; }
    /// <summary>A domain controller, or the domain name itself (which resolves to one).</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 636;
    public DirectorySecurity Security { get; set; } = DirectorySecurity.Ldaps;
    /// <summary>DNS name of the domain, e.g. <c>corp.example.com</c>. Completes a bare user name into user@domain.</summary>
    public string? Domain { get; set; }
    /// <summary>Where to look for users, e.g. <c>DC=corp,DC=example,DC=com</c>.</summary>
    public string? BaseDn { get; set; }
    /// <summary>When set, only direct members of this group (by distinguished name) may sign in.</summary>
    public string? RequiredGroupDn { get; set; }
    /// <summary>The attribute holding a user's department. Read at every sign-in when the user has one.</summary>
    public string DepartmentAttribute { get; set; } = "department";
    /// <summary>When set, direct members of this group are managers in Taskel, and others are not.</summary>
    public string? ManagerGroupDn { get; set; }
    /// <summary>When set, direct members of this group are administrators in Taskel, and others are not.</summary>
    public string? AdminGroupDn { get; set; }
    /// <summary>
    /// Whether the groups a user belongs to become groups in Taskel that tasks can be given to.
    /// They are learned at sign-in, so a group shows the members who have signed in to Taskel.
    /// </summary>
    public bool SyncGroups { get; set; } = true;
    /// <summary>When set, only groups under this container (e.g. <c>OU=Teams,DC=corp,DC=example,DC=com</c>) are brought in.</summary>
    public string? GroupsBaseDn { get; set; }
    public int TimeoutSeconds { get; set; } = 10;

    public bool IsUsable =>
        Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Domain) && !string.IsNullOrWhiteSpace(BaseDn);
}

/// <summary>Bound from the <c>Auth</c> configuration section.</summary>
public class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Whether anyone can create a local (email + password) account.</summary>
    public bool AllowLocalRegistration { get; set; } = true;
    /// <summary>Sign-in attempts allowed per minute from one address, to slow password guessing and protect AD lockout.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;
    /// <summary>
    /// Accounts with these email addresses are always administrators. This is how the first
    /// administrator is named; they can then give roles to others from the interface.
    /// </summary>
    public string[] AdminEmails { get; set; } = [];

    /// <summary>The role a user acts with: the configured administrators, otherwise the account's own role.</summary>
    public UserRole EffectiveRole(User user) => IsConfiguredAdmin(user) ? UserRole.Admin : user.Role;

    public bool IsConfiguredAdmin(User user) =>
        AdminEmails.Any(email => string.Equals(email?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase));
}
