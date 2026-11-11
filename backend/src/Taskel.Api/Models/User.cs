using System.ComponentModel.DataAnnotations;

namespace Taskel.Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Stored trimmed and lower-cased; unique.</summary>
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ids of the dashboard widgets the user shows, in order. Null means the default layout.</summary>
    public List<string>? DashboardWidgets { get; set; }
    /// <summary>Whether to show the overdue / due-today reminder when the user opens the app.</summary>
    public bool ShowDueAlert { get; set; } = true;
}

public record RegisterRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public record LoginRequest(
    [Required, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password);

public record UserResponse(Guid Id, string Email);
