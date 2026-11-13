using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

public enum UserRole
{
    User = 0,
    /// <summary>Sees the workload and tasks of the people in their own department.</summary>
    Manager = 1,
    /// <summary>Sets roles and departments, and sees every department.</summary>
    Admin = 2
}

public class User
{
    public const int MaxDepartmentLength = 100;

    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Stored trimmed and lower-cased; unique.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Empty for directory accounts, which have no local password.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Name shown to other users; taken from the directory. Falls back to the email.</summary>
    public string? DisplayName { get; set; }
    /// <summary>The account's objectGUID in Active Directory. Null for local accounts.</summary>
    public string? DirectoryId { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    /// <summary>The department the user belongs to. A manager sees the people who share theirs.</summary>
    public string? Department { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ids of the dashboard widgets the user shows, in order. Null means the default layout.</summary>
    public List<string>? DashboardWidgets { get; set; }
    /// <summary>Whether to show the overdue / due-today reminder when the user opens the app.</summary>
    /// <summary>Whether Taskel emails this user when a task reaches them.</summary>
    public bool EmailNotifications { get; set; } = true;
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

public record UserResponse(
    Guid Id,
    string Email,
    string? DisplayName = null,
    bool IsDirectoryAccount = false,
    UserRole Role = UserRole.User,
    string? Department = null)
{
    /// <param name="role">The user's effective role, which may come from configuration rather than the account.</param>
    public static UserResponse From(User user, UserRole role) =>
        new(user.Id, user.Email, user.DisplayName ?? user.Email, user.DirectoryId is not null, role, user.Department);
}

/// <summary>An account as the administrator sees it.</summary>
/// <param name="RoleIsFixed">True when the role comes from configuration and cannot be changed here.</param>
public record AdminUserResponse(
    Guid Id, string Email, string DisplayName, UserRole Role, string? Department, bool IsDirectoryAccount, bool RoleIsFixed);

/// <remarks>
/// The role must be sent: left out, it would read as <see cref="UserRole.User"/> and demote the account.
/// </remarks>
public record UpdateUserRequest(
    [property: JsonRequired][EnumDataType(typeof(UserRole))] UserRole Role,
    [StringLength(User.MaxDepartmentLength), SafeText] string? Department);

/// <summary>What the sign-in screen should offer.</summary>
public record AuthOptionsResponse(bool LocalRegistration, bool ActiveDirectory, string? ActiveDirectoryDomain);

/// <param name="EmailOnNewTask">Whether the user wants an email when a task reaches them.</param>
/// <param name="Address">Where those emails go: the account's address.</param>
/// <param name="Available">Whether the server can send them at all (SMTP configured, notifications not switched off).</param>
public record NotificationSettingsResponse(bool EmailOnNewTask, string Address, bool Available);

public record UpdateNotificationSettingsRequest([property: JsonRequired] bool EmailOnNewTask);
