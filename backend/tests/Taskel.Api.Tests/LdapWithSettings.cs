using Microsoft.Extensions.Logging.Abstractions;
using Taskel.Api.Directory;

namespace Taskel.Api.Tests;

/// <summary>The real LDAP authenticator, run against fixed settings instead of the ones saved in Taskel.</summary>
public sealed class LdapWithSettings(DirectoryOptions options)
{
    private readonly LdapDirectoryAuthenticator _inner = new(null!, NullLogger<LdapDirectoryAuthenticator>.Instance);

    public Task<DirectoryUser?> AuthenticateAsync(string username, string password, CancellationToken ct = default) =>
        _inner.AuthenticateAsync(options, username, password, ct);
}
