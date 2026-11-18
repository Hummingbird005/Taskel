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
    /// <summary>
    /// The group the task was given to. While it has no assignee, every member sees it and any of
    /// them can take it; once taken, it stays linked to the group so it can be handed back.
    /// </summary>
    [JsonIgnore]
    public Guid? GroupId { get; set; }
    [JsonIgnore]
    public Group? Group { get; set; }
    /// <summary>Each time the task was passed on. People who passed it on can still read it.</summary>
    [JsonIgnore]
    public List<TaskForward> Forwards { get; set; } = [];
    /// <summary>Files attached to the task.</summary>
    [JsonIgnore]
    public List<TaskAttachment> Attachments { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>
    /// First day of the period the task is worked on. Optional: together with <see cref="DueDate"/>
    /// it gives the task an interval; without it the task falls on its due date alone.
    /// </summary>
    public DateOnly? StartDate { get; set; }
    /// <summary>Calendar date the task is due, without a time or time zone. The last day of its interval.</summary>
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
    /// <summary>
    /// What the person who sent the task gave as guidance, e.g. when distributing an email from Outlook
    /// to several people. Shown above the task's message.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>The response written when the task was completed.</summary>
    public string? CompletionResponse { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Why the task is on hold. Set together with <see cref="WaitingSince"/>; both null while it is not.</summary>
    public string? WaitingReason { get; set; }
    /// <summary>When the task was put on hold. A task on hold is still open: it counts as open work everywhere.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>Where the task stands, as people see it.</summary>
    [JsonIgnore]
    public TaskState State
    {
        get
        {
            if (IsCompleted) return TaskState.Completed;
            return WaitingSince is not null ? TaskState.Waiting : TaskState.Open;
        }
    }

    /// <summary>True when completing this task must go through a response that is emailed back.</summary>
    [JsonIgnore]
    public bool RequiresCompletionReply => !string.IsNullOrEmpty(SourceEmailAddress);

    /// <summary>
    /// The forward the task must go back along when it is completed: the latest one whose sender asked
    /// for it back and has not had it back yet. Null when completing really completes it.
    /// </summary>
    public TaskForward? PendingReturn() =>
        Forwards.Where(f => f.ReturnPending && !f.IsReturn).OrderByDescending(f => f.At).FirstOrDefault();

    /// <summary>
    /// Where the completion message of an email task is sent, for someone with <paramref name="access"/>:
    /// the email's sender when the task's creator completes it, otherwise the creator, who sent the email
    /// to Taskel. Null for a task that did not come from an email.
    /// </summary>
    public string? ReplyAddressFor(TaskAccess access)
    {
        if (!RequiresCompletionReply) return null;
        return access == TaskAccess.Creator ? SourceEmailAddress : Owner?.Email;
    }
}

public record CreateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1), SafeText] string Title,
    [StringLength(2000), SafeText(Multiline = true)] string? Description,
    [DueDateRange] DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null,
    /// <summary>User to assign the task to. Omit to keep it yourself.</summary>
    Guid? AssigneeId = null,
    /// <summary>Group to give the task to, instead of a user. Any member can then take it.</summary>
    Guid? GroupId = null,
    /// <summary>First day of the task's interval. Needs a due date on or after it.</summary>
    [DueDateRange] DateOnly? StartDate = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        TaskInterval.Validate(StartDate, DueDate);
}

/// <summary>Full replacement of a task: omitted optional fields are reset (no due date, medium priority, no tags).</summary>
public record UpdateTaskRequest(
    [Required, StringLength(200, MinimumLength = 1), SafeText] string Title,
    [StringLength(2000), SafeText(Multiline = true)] string? Description,
    // Required: left out, it would read as false and reopen a completed task.
    [property: JsonRequired] bool IsCompleted,
    [DueDateRange] DateOnly? DueDate = null,
    [EnumDataType(typeof(TaskPriority))] TaskPriority Priority = TaskPriority.Medium,
    [TagList] List<string>? Tags = null,
    [DueDateRange] DateOnly? StartDate = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        TaskInterval.Validate(StartDate, DueDate);
}

/// <summary>The period a task occupies in a calendar, and the rule for giving it one.</summary>
public static class TaskInterval
{
    public static IEnumerable<ValidationResult> Validate(DateOnly? start, DateOnly? due)
    {
        if (start is null) yield break;
        if (due is null)
            yield return new ValidationResult("A start date needs a due date to end the interval.", [nameof(TaskItem.StartDate)]);
        else if (start > due)
            yield return new ValidationResult("The start date cannot be after the due date.", [nameof(TaskItem.StartDate)]);
    }
}

/// <summary>Completes a task. <see cref="Response"/> is required for tasks created from an email.</summary>
public record CompleteTaskRequest([StringLength(4000), SafeText(Multiline = true)] string? Response = null);

/// <summary>Where a task stands.</summary>
public enum TaskState
{
    /// <summary>Being worked on, or not started.</summary>
    Open,
    /// <summary>On hold, with a reason: waiting on someone or something.</summary>
    Waiting,
    Completed
}

/// <summary>Puts a task on hold. The reason is required: it tells everyone involved what the task waits for.</summary>
public record WaitTaskRequest([Required, StringLength(1000, MinimumLength = 1), SafeText(Multiline = true)] string Reason);

/// <summary>
/// Assigns a task to a user or to a group (not both). With neither, it returns to its creator.
/// </summary>
public record AssignTaskRequest(Guid? AssigneeId = null, Guid? GroupId = null);

/// <summary>Passes a received task on to another user or to a group: exactly one of the two.</summary>
/// <param name="ReturnToMe">
/// When the person (or group member) who gets the task completes it, it comes back to whoever forwarded
/// it, with their message, instead of being completed.
/// </param>
public record ForwardTaskRequest(
    Guid? AssigneeId = null,
    Guid? GroupId = null,
    [StringLength(500), SafeText(Multiline = true)] string? Note = null,
    bool ReturnToMe = false);

/// <summary>What the person asking may do with a task.</summary>
public enum TaskAccess
{
    /// <summary>Created it: may do everything.</summary>
    Creator,
    /// <summary>Has it assigned: may edit, complete and pass it on.</summary>
    Assignee,
    /// <summary>Belongs to the group it was given to: may read it and take it.</summary>
    Group,
    /// <summary>May only read it: passed it on earlier, or manages the person who has it.</summary>
    Viewer
}

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
    DateOnly? StartDate,
    DateOnly? DueDate,
    TaskPriority Priority,
    List<string> Tags,
    DateTime CreatedAt,
    string? SourceEmailAddress,
    string? CompletionResponse,
    DateTime? CompletedAt,
    TaskState State,
    string? WaitingReason,
    DateTime? WaitingSince,
    UserSummary CreatedBy,
    UserSummary? Assignee,
    GroupSummary? Group,
    IReadOnlyList<ForwardResponse> Forwards,
    IReadOnlyList<AttachmentResponse> Attachments,
    TaskAccess Access,
    string? Instructions = null,
    string? ReplyTo = null,
    UserSummary? ReturnsTo = null)
{
    /// <summary>The task's people, group and forwards must be loaded.</summary>
    /// <remarks>
    /// <see cref="ReturnsTo"/> and <see cref="ReplyTo"/> are for the person asking: whether completing
    /// the task sends it back to someone, and where the completion message is emailed.
    /// </remarks>
    public static TaskResponse From(TaskItem task, TaskAccess access)
    {
        var returnsTo = access == TaskAccess.Assignee && !task.IsCompleted ? task.PendingReturn()?.From : null;
        return Create(task, access, returnsTo is null ? null : UserSummary.From(returnsTo),
            returnsTo is null ? task.ReplyAddressFor(access) : null);
    }

    private static TaskResponse Create(TaskItem task, TaskAccess access, UserSummary? returnsTo, string? replyTo) => new(
        task.Id, task.Title, task.Description, task.IsCompleted, task.StartDate, task.DueDate, task.Priority, task.Tags,
        task.CreatedAt, task.SourceEmailAddress, task.CompletionResponse, task.CompletedAt,
        task.State, task.WaitingReason, task.WaitingSince,
        UserSummary.From(task.Owner ?? throw new InvalidOperationException("Task owner is not loaded.")),
        task.Assignee is null ? null : UserSummary.From(task.Assignee),
        task.Group is null ? null : GroupSummary.From(task.Group),
        task.Forwards
            .OrderBy(f => f.At)
            .Select(f => new ForwardResponse(
                UserSummary.From(f.From ?? throw new InvalidOperationException("Forward sender is not loaded.")),
                f.ToUser is null ? null : UserSummary.From(f.ToUser),
                f.ToGroup is null ? null : GroupSummary.From(f.ToGroup),
                f.Note,
                f.At,
                f.IsReturn,
                f.ReturnPending))
            .ToList(),
        task.Attachments.OrderBy(a => a.UploadedAt).Select(AttachmentResponse.From).ToList(),
        access,
        task.Instructions,
        replyTo,
        returnsTo);
}
