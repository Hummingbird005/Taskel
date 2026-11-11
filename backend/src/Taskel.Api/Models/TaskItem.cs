using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Taskel.Api.Models;

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Owner of the task. Never exposed through the API.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record CreateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description);

public record UpdateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    bool IsCompleted);
