using System.Collections.Concurrent;
using Taskel.Api.Directory;

namespace Taskel.Api.Tests;

/// <summary>Stands in for Active Directory: accounts are registered by the tests that need them.</summary>
public class FakeDirectory : IDirectoryAuthenticator
{
    private readonly ConcurrentDictionary<string, (string Password, DirectoryUser User)> _accounts = new();

    public bool Unavailable { get; set; }

    public DirectoryUser AddAccount(string username, string password, string? email = null, string? displayName = null)
    {
        var user = new DirectoryUser(Guid.NewGuid().ToString(), email ?? $"{username}@corp.example.com", displayName);
        _accounts[username] = (password, user);
        return user;
    }

    public void Replace(string username, string password, DirectoryUser user) => _accounts[username] = (password, user);

    public Task<DirectoryUser?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (Unavailable) throw new DirectoryUnavailableException("down");
        return Task.FromResult(
            _accounts.TryGetValue(username, out var account) && account.Password == password ? account.User : null);
    }
}
