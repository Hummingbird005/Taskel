namespace Taskel.Api.Directory;

/// <summary>A directory account that proved its password.</summary>
/// <param name="DirectoryId">The account's objectGUID: stable across renames and email changes.</param>
/// <param name="Department">The user's department in the directory, when it has one.</param>
/// <param name="IsManager">Membership of the configured manager group; null when no group is configured.</param>
/// <param name="Groups">The groups the user is a direct member of; null when groups are not brought into Taskel.</param>
/// <param name="IsAdmin">Membership of the configured administrator group; null when no group is configured.</param>
public record DirectoryUser(
    string DirectoryId,
    string Email,
    string? DisplayName,
    string? Department = null,
    bool? IsManager = null,
    bool? IsAdmin = null,
    IReadOnlyList<DirectoryGroup>? Groups = null);

/// <summary>A directory group a user belongs to, which tasks can be given to in Taskel.</summary>
public record DirectoryGroup(string Dn, string Name);

public interface IDirectoryAuthenticator
{
    /// <summary>
    /// Checks a user name and password against the directory.
    /// Returns null when they are wrong, or when the account may not use Taskel.
    /// </summary>
    /// <exception cref="DirectoryUnavailableException">The directory could not be reached or is misconfigured.</exception>
    Task<DirectoryUser?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}

public class DirectoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
