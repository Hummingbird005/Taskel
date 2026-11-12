using System.Text;
using System.Text.RegularExpressions;

namespace Taskel.Api.Directory;

/// <summary>A user name as the directory needs it: the short logon name and the user@domain form.</summary>
public record DirectoryLogin(string AccountName, string UserPrincipalName);

public static partial class DirectoryNames
{
    /// <summary>
    /// Turns the distinguished names from <c>memberOf</c> into groups for Taskel, named after their
    /// common name. Keeps only those under <paramref name="baseDn"/> when one is given.
    /// </summary>
    public static IReadOnlyList<DirectoryGroup> TaskGroups(IEnumerable<string> groupDns, string? baseDn)
    {
        var suffix = string.IsNullOrWhiteSpace(baseDn) ? null : "," + baseDn.Trim();
        var groups = new List<DirectoryGroup>();
        foreach (var raw in groupDns)
        {
            var dn = raw.Trim();
            if (dn.Length is 0 or > 1024) continue;
            if (suffix is not null && !dn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Validation.SafeText.Clean(CommonName(dn), multiline: false).Trim();
            if (name.Length == 0) continue;
            if (name.Length > Models.Group.MaxNameLength) name = name[..Models.Group.MaxNameLength].TrimEnd();
            if (!groups.Any(g => string.Equals(g.Dn, dn, StringComparison.OrdinalIgnoreCase)))
                groups.Add(new DirectoryGroup(dn, name));
        }
        return groups;
    }

    /// <summary>The value of a distinguished name's first component, unescaped: <c>CN=Sales\, EU,OU=…</c> gives <c>Sales, EU</c>.</summary>
    public static string CommonName(string dn)
    {
        var start = dn.IndexOf('=') + 1;
        var bytes = new List<byte>();
        for (var i = start; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == ',') break;
            if (c == '\\' && i + 1 < dn.Length)
            {
                if (i + 2 < dn.Length && Uri.IsHexDigit(dn[i + 1]) && Uri.IsHexDigit(dn[i + 2]))
                {
                    bytes.Add(Convert.ToByte(dn.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(dn[i + 1].ToString()));
                    i++;
                }
                continue;
            }
            bytes.AddRange(Encoding.UTF8.GetBytes(char.IsHighSurrogate(c) && i + 1 < dn.Length ? dn.Substring(i++, 2) : c.ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

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
