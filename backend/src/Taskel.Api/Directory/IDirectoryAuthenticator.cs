namespace Taskel.Api.Directory;

/// <summary>A directory account that proved its password.</summary>
/// <param name="DirectoryId">The account's objectGUID: stable across renames and email changes.</param>
public record DirectoryUser(string DirectoryId, string Email, string? DisplayName);

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
