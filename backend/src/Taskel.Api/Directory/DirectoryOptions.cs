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
}
