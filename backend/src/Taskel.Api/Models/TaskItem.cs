using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Taskel.Api.Validation;

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
    /// <summary>The user who created the task. Only they can delete or reassign it.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public User? Owner { get; set; }
    /// <summary>
    /// The user the task is assigned to, when that is someone other than its creator.
    /// Null means the creator keeps it. The assignee can see, edit and complete the task.
    /// </summary>
    [JsonIgnore]
    public Guid? AssigneeId { get; set; }
    [JsonIgnore]
    public User? Assignee { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>Calendar date the task is due, without a time or time zone.</summary>
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    /// <summary>Normalized (trimmed, lower-case, distinct) tags. See <see cref="TagList"/>.</summary>
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Address of the person who sent the email this task was created from. When set, completing
    /// the task requires a response, which is emailed to this address as a reply.
    /// </summary>
    public string? SourceEmailAddress { get; set; }
    /// <summary>Subject of the source email, used for the reply's "Re:" subject.</summary>
    [JsonIgnore]
    public string? SourceEmailSubject { get; set; }
    /// <summary>Message-ID of the source email, so the reply lands in the same thread.</summary>
    [JsonIgnore]
    public string? SourceEmailMessageId { get; set; }

    /// <summary>The response written when the task was completed.</summary>
    public string? CompletionResponse { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>True when completing this task must go through a response that is emailed back.</summary>
    [JsonIgnore]
    public bool RequiresCompletionReply => !string.IsNullOrEmpty(SourceEmailAddress);
}

public record CreateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1), SafeText] string Title,
    [StringLength(2000), SafeText(Multiline = true)] string? Description,
    [DueDateRange] DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null,
    /// <summary>User to assign the task to. Omit to keep it yourself.</summary>
    Guid? AssigneeId = null);

/// <summary>Full replacement of a task: omitted optional fields are reset (no due date, medium priority, no tags).</summary>
public record UpdateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1), SafeText] string Title,
    [StringLength(2000), SafeText(Multiline = true)] string? Description,
    bool IsCompleted,
    [DueDateRange] DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null);

/// <summary>Completes a task. <see cref="Response"/> is required for tasks created from an email.</summary>
public record CompleteTaskRequest([StringLength(4000), SafeText(Multiline = true)] string? Response = null);

/// <summary>Assigns a task. A null <see cref="AssigneeId"/> returns it to its creator.</summary>
public record AssignTaskRequest(Guid? AssigneeId);

/// <summary>A user as shown to other users: for pickers and "assigned to" labels.</summary>
public record UserSummary(Guid Id, string Email, string DisplayName)
{
    public static UserSummary From(User user) => new(user.Id, user.Email, user.DisplayName ?? user.Email);
}

/// <summary>A task as returned by the API.</summary>
public record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateOnly? DueDate,
    TaskPriority Priority,
    List<string> Tags,
    DateTime CreatedAt,
    string? SourceEmailAddress,
    string? CompletionResponse,
    DateTime? CompletedAt,
    UserSummary CreatedBy,
    UserSummary? Assignee)
{
    /// <summary>The task's <see cref="TaskItem.Owner"/> (and assignee, if any) must be loaded.</summary>
    public static TaskResponse From(TaskItem task) => new(
        task.Id, task.Title, task.Description, task.IsCompleted, task.DueDate, task.Priority, task.Tags,
        task.CreatedAt, task.SourceEmailAddress, task.CompletionResponse, task.CompletedAt,
        UserSummary.From(task.Owner ?? throw new InvalidOperationException("Task owner is not loaded.")),
        task.Assignee is null ? null : UserSummary.From(task.Assignee));
}
