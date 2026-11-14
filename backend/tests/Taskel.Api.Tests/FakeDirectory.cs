using System.Collections.Concurrent;
using Taskel.Api.Directory;

namespace Taskel.Api.Tests;

/// <summary>Stands in for Active Directory: accounts are registered by the tests that need them.</summary>
public class FakeDirectory : IDirectoryAuthenticator
{
    private readonly ConcurrentDictionary<string, (string Password, DirectoryUser User)> _accounts = new();

    public bool Unavailable { get; set; }

    public DirectoryUser AddAccount(string username, string password, string? email = null, string? displayName = null,
        string? department = null, bool? isManager = null, bool? isAdmin = null, params string[] groups)
    {
        var user = new DirectoryUser(
            Guid.NewGuid().ToString(), email ?? $"{username}@corp.example.com", displayName, department, isManager, isAdmin,
            groups.Select(name => new DirectoryGroup($"CN={name},OU=Groups,DC=corp,DC=example,DC=com", name)).ToList());
        _accounts[username] = (password, user);
        return user;
    }

    public void Replace(string username, string password, DirectoryUser user) => _accounts[username] = (password, user);

    /// <summary>The settings the last test sign-in was made with.</summary>
    public DirectoryOptions? LastTestedWith { get; private set; }

    public Task<DirectoryUser?> AuthenticateAsync(DirectoryOptions directory, string username, string password, CancellationToken ct = default)
    {
        LastTestedWith = directory;
        return AuthenticateAsync(username, password, ct);
    }

    public Task<DirectoryUser?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (Unavailable) throw new DirectoryUnavailableException("down");
        return Task.FromResult(
            _accounts.TryGetValue(username, out var account) && account.Password == password ? account.User : null);
    }
}
