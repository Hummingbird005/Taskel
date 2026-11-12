using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Stored trimmed and lower-cased; unique.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Empty for directory accounts, which have no local password.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Name shown to other users; taken from the directory. Falls back to the email.</summary>
    public string? DisplayName { get; set; }
    /// <summary>The account's objectGUID in Active Directory. Null for local accounts.</summary>
    public string? DirectoryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ids of the dashboard widgets the user shows, in order. Null means the default layout.</summary>
    public List<string>? DashboardWidgets { get; set; }
    /// <summary>Whether to show the overdue / due-today reminder when the user opens the app.</summary>
    public bool ShowDueAlert { get; set; } = true;
}

public record RegisterRequest(
    [Required, StringLength(StrictEmailAttribute.MaxLength), StrictEmail] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public record LoginRequest(
    [Required, StringLength(StrictEmailAttribute.MaxLength), SafeText] string Email,
    [Required, StringLength(128)] string Password);

/// <summary>Sign-in with a domain account: <c>user</c>, <c>DOMAIN\user</c> or <c>user@domain</c>.</summary>
public record DirectoryLoginRequest(
    [Required, StringLength(128), SafeText] string Username,
    [Required, StringLength(256)] string Password);

public record UserResponse(Guid Id, string Email, string? DisplayName = null, bool IsDirectoryAccount = false)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.DisplayName ?? user.Email, user.DirectoryId is not null);
}

/// <summary>What the sign-in screen should offer.</summary>
public record AuthOptionsResponse(bool LocalRegistration, bool ActiveDirectory, string? ActiveDirectoryDomain);
