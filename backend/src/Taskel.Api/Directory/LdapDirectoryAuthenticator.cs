using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;
using Taskel.Api.Validation;

namespace Taskel.Api.Directory;

/// <summary>
/// Signs users in against Active Directory over LDAP. It binds as the user themselves
/// (user@domain plus their password), so no service account is needed, then reads the
/// user's own entry for the email, display name and objectGUID.
/// </summary>
public class LdapDirectoryAuthenticator(IOptions<DirectoryOptions> options, ILogger<LdapDirectoryAuthenticator> logger)
    : IDirectoryAuthenticator
{
    private static readonly string[] BaseAttributes = ["objectGUID", "mail", "userPrincipalName", "displayName", "memberOf"];

    public async Task<DirectoryUser?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var directory = options.Value;
        if (!directory.IsUsable) throw new DirectoryUnavailableException("Active Directory sign-in is not configured.");

        // An empty password makes an LDAP server treat the bind as anonymous and report success.
        if (string.IsNullOrEmpty(password)) return null;
        if (!DirectoryNames.TryParse(username, directory.Domain!, out var login)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(directory.TimeoutSeconds, 1, 60)));

        try
        {
            var connectionOptions = new LdapConnectionOptions();
            if (directory.Security == DirectorySecurity.Ldaps) connectionOptions.UseSsl();

            using var connection = new LdapConnection(connectionOptions);
            await connection.ConnectAsync(directory.Host, directory.Port, timeout.Token);
            if (directory.Security == DirectorySecurity.StartTls) await connection.StartTlsAsync(timeout.Token);

            try
            {
                await connection.BindAsync(login.UserPrincipalName, password, timeout.Token);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                return null;
            }

            var entry = await FindOwnEntryAsync(connection, directory, login, timeout.Token);
            return entry is null ? null : ToDirectoryUser(entry, directory, login);
        }
        catch (LdapException ex)
        {
            logger.LogError(ex, "Active Directory request to {Host}:{Port} failed", directory.Host, directory.Port);
            throw new DirectoryUnavailableException("Active Directory could not be reached.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Active Directory request to {Host}:{Port} timed out", directory.Host, directory.Port);
            throw new DirectoryUnavailableException("Active Directory did not answer in time.");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException
                                       or System.Security.Authentication.AuthenticationException)
        {
            logger.LogError(ex, "Connecting to Active Directory at {Host}:{Port} failed", directory.Host, directory.Port);
            throw new DirectoryUnavailableException("Active Directory could not be reached.", ex);
        }
    }

    private static async Task<LdapEntry?> FindOwnEntryAsync(
        LdapConnection connection, DirectoryOptions directory, DirectoryLogin login, CancellationToken ct)
    {
        var filter = "(&(objectCategory=person)(objectClass=user)(|"
                     + $"(userPrincipalName={DirectoryNames.EscapeFilterValue(login.UserPrincipalName)})"
                     + $"(sAMAccountName={DirectoryNames.EscapeFilterValue(login.AccountName)})))";

        string[] attributes = DepartmentAttributeOf(directory) is { } department
            ? [.. BaseAttributes, department]
            : BaseAttributes;
        var results = await connection.SearchAsync(directory.BaseDn, LdapConnection.ScopeSub, filter, attributes, false, ct);
        LdapEntry? found = null;
        while (await results.HasMoreAsync(ct))
        {
            LdapEntry entry;
            try
            {
                entry = await results.NextAsync(ct);
            }
            catch (LdapReferralException)
            {
                continue; // Referrals to other partitions are not followed.
            }
            if (found is not null) return null; // Ambiguous: refuse rather than pick one.
            found = entry;
        }
        return found;
    }

    private DirectoryUser? ToDirectoryUser(LdapEntry entry, DirectoryOptions directory, DirectoryLogin login)
    {
        var groups = entry.GetOrDefault("memberOf", null)?.StringValueArray ?? [];
        bool? MemberOf(string? groupDn) => string.IsNullOrWhiteSpace(groupDn)
            ? null
            : groups.Any(group => string.Equals(group, groupDn.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(directory.RequiredGroupDn))
        {
            if (MemberOf(directory.RequiredGroupDn) != true)
            {
                logger.LogWarning("{User} signed in to the directory but is not in the required group", login.UserPrincipalName);
                return null;
            }
        }

        var guidBytes = entry.GetOrDefault("objectGUID", null)?.ByteValue;
        if (guidBytes is not { Length: 16 })
        {
            logger.LogError("The directory entry for {User} has no objectGUID", login.UserPrincipalName);
            return null;
        }

        var email = FirstValidEmail(
            entry.GetOrDefault("mail", null)?.StringValue,
            entry.GetOrDefault("userPrincipalName", null)?.StringValue,
            login.UserPrincipalName);
        if (email is null)
        {
            logger.LogWarning("{User} has no usable email address in the directory", login.UserPrincipalName);
            return null;
        }

        var displayName = SafeText.Clean(entry.GetOrDefault("displayName", null)?.StringValue, multiline: false).Trim();
        var department = DepartmentAttributeOf(directory) is { } attribute
            ? SafeText.Clean(entry.GetOrDefault(attribute, null)?.StringValue, multiline: false).Trim()
            : string.Empty;
        return new DirectoryUser(
            new Guid(guidBytes).ToString(),
            email,
            displayName.Length is > 0 and <= 256 ? displayName : null,
            department.Length is > 0 and <= Models.User.MaxDepartmentLength ? department : null,
            MemberOf(directory.ManagerGroupDn),
            MemberOf(directory.AdminGroupDn));
    }

    /// <summary>The department attribute to read, or null when it is not a plain attribute name.</summary>
    private static string? DepartmentAttributeOf(DirectoryOptions directory)
    {
        var name = directory.DepartmentAttribute?.Trim();
        return !string.IsNullOrEmpty(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? name : null;
    }

    private static string? FirstValidEmail(params string?[] candidates) =>
        candidates
            .Select(candidate => candidate?.Trim().ToLowerInvariant())
            .FirstOrDefault(StrictEmailAttribute.IsValidAddress);
}
