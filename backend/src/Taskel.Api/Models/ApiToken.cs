using System.ComponentModel.DataAnnotations;

namespace Taskel.Api.Models;

/// <summary>
/// A personal access token for clients that cannot use the browser session (e.g. the Outlook macro).
/// Only a SHA-256 hash of the token is stored; the token itself is shown once, at creation.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}

public record CreateApiTokenRequest([Required, StringLength(50, MinimumLength = 1)] string Name);

public record ApiTokenResponse(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>Returned only when a token is created; <see cref="Token"/> cannot be retrieved again.</summary>
public record CreatedApiTokenResponse(Guid Id, string Name, DateTime CreatedAt, string Token);
