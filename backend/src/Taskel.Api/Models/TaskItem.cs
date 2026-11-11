using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Taskel.Api.Models;

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Owner of the task. Never exposed through the API.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>Calendar date the task is due, without a time or time zone.</summary>
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    /// <summary>Normalized (trimmed, lower-case, distinct) tags. See <see cref="TagList"/>.</summary>
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record CreateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null);

/// <summary>Full replacement of a task: omitted optional fields are reset (no due date, medium priority, no tags).</summary>
public record UpdateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    bool IsCompleted,
    DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null);
