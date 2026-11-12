using System.Text;
using System.Text.RegularExpressions;

namespace Taskel.Api.Directory;

/// <summary>A user name as the directory needs it: the short logon name and the user@domain form.</summary>
public record DirectoryLogin(string AccountName, string UserPrincipalName);

public static partial class DirectoryNames
{
    /// <summary>
    /// Accepts <c>user</c>, <c>DOMAIN\user</c> or <c>user@domain</c> and returns both name forms.
    /// Anything outside a conservative character set is refused, so nothing unexpected reaches LDAP.
    /// </summary>
    public static bool TryParse(string? input, string defaultDomain, out DirectoryLogin login)
    {
        login = new DirectoryLogin(string.Empty, string.Empty);
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        string account;
        string domain = defaultDomain;
        var backslash = value.IndexOf('\\');
        var at = value.IndexOf('@');
        if (backslash >= 0)
        {
            // DOMAIN\user: the NetBIOS domain is not needed to bind, the configured domain is used.
            if (at >= 0 || !DomainLabel().IsMatch(value[..backslash])) return false;
            account = value[(backslash + 1)..];
        }
        else if (at >= 0)
        {
            account = value[..at];
            domain = value[(at + 1)..];
        }
        else
        {
            account = value;
        }

        if (!AccountName().IsMatch(account) || !DomainName().IsMatch(domain)) return false;
        login = new DirectoryLogin(account, $"{account}@{domain}");
        return true;
    }

    /// <summary>Escapes a value for use inside an LDAP search filter (RFC 4515).</summary>
    public static string EscapeFilterValue(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString()
            });
        }
        return escaped.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$")]
    private static partial Regex AccountName();

    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)*$")]
    private static partial Regex DomainName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]{0,14}$")]
    private static partial Regex DomainLabel();
}
