using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

/// <summary>What a message in a task's conversation records.</summary>
public enum TaskMessageKind
{
    /// <summary>Written by someone working on the task.</summary>
    Note,
    /// <summary>The task was distributed to someone, with this message.</summary>
    Distributed,
    /// <summary>Someone it was distributed to completed their task with this answer.</summary>
    Answered,
    /// <summary>Everyone it was distributed to has answered: the task is open again.</summary>
    AllAnswered,
    /// <summary>The distributor sent one person's task back, with this message.</summary>
    SentBack,
    /// <summary>The distributor closed one person's task without waiting for the answer.</summary>
    Closed,
    /// <summary>A manager moved the task's due date; the text says from what to what.</summary>
    DueDateChanged,
    /// <summary>
    /// A manager moved the task to another day or level; the text says from what to what, and why.
    /// </summary>
    Rescheduled
}

/// <summary>
/// One entry in a task's conversation. Distribution messages are kept on the task that was distributed
/// (<see cref="TaskId"/>), with the copy they concern in <see cref="RelatedTaskId"/>: the distributor reads them all,
/// and each recipient reads those about their own copy.
/// </summary>
public class TaskMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid? RelatedTaskId { get; set; }
    public TaskItem? RelatedTask { get; set; }
    /// <summary>Null for what Taskel itself records, and when the author's account was deleted.</summary>
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
    public TaskMessageKind Kind { get; set; }
    public string? Text { get; set; }
    /// <summary>Stored to the microsecond, PostgreSQL's precision, so the order is the same when read back.</summary>
    public DateTime At { get; set; } = DateTime.UnixEpoch.AddTicks((DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10 * 10);
}

/// <summary>The task a distributed copy came from, as its recipient sees it.</summary>
public record ParentTaskSummary(Guid Id, string Title);

/// <summary>One copy of a distributed task: who has it, where it stands, and their answer.</summary>
public record DistributionResponse(
    Guid TaskId, UserSummary? Assignee, GroupSummary? Group, TaskState State, DateOnly? DueDate, string? Answer,
    DateTime? CompletedAt)
{
    public static DistributionResponse From(TaskItem copy) => new(
        copy.Id,
        copy.Assignee is null ? null : UserSummary.From(copy.Assignee),
        copy.Group is null ? null : GroupSummary.From(copy.Group),
        copy.State,
        copy.DueDate,
        copy.CompletionResponse,
        copy.CompletedAt);
}

/// <param name="Recipients">User ids, or <c>group:</c> and a group id; each gets a copy of the task.</param>
/// <param name="Message">What the recipients should do; shown above the task's message.</param>
/// <param name="DueDate">The copies' due date; the task's own when not given.</param>
public record DistributeTaskRequest(
    [Required, MinLength(1), MaxLength(50)] List<string> Recipients,
    [StringLength(2000), SafeText(Multiline = true)] string? Message = null,
    [DueDateRange] DateOnly? DueDate = null);

public record SendBackRequest([Required, StringLength(4000), SafeText(Multiline = true)] string Message);

/// <param name="RelatedTaskId">On a distributed task: the copy the message is for. Without it, a note on the task.</param>
public record PostMessageRequest(
    [Required, StringLength(4000), SafeText(Multiline = true)] string Text,
    Guid? RelatedTaskId = null);

/// <param name="Related">The copy of a distributed task the message concerns, and who has it.</param>
/// <param name="Attachments">Files that came with an answer, as the reader reaches them on their own task.</param>
public record TaskMessageResponse(
    Guid Id, TaskMessageKind Kind, UserSummary? Author, string? Text, DateTime At, RelatedCopy? Related,
    IReadOnlyList<AttachmentResponse>? Attachments = null)
{
    public static TaskMessageResponse From(TaskMessage message, IEnumerable<TaskAttachment>? files = null) => new(
        message.Id, message.Kind,
        message.Author is null ? null : UserSummary.From(message.Author),
        message.Text, message.At,
        message.RelatedTask is { } copy ? new RelatedCopy(copy.Id, RecipientName(copy)) : null,
        files?.Select(AttachmentResponse.From).ToList() is { Count: > 0 } list ? list : null);

    private static string RecipientName(TaskItem copy) =>
        copy.Assignee is { } user ? user.DisplayName ?? user.Email
        : copy.Group is { } group ? group.Name
        : string.Empty;
}

public record RelatedCopy(Guid TaskId, string Recipient);

/// <summary>Where a manager moves a task: its day and its place in that day, and why.</summary>
/// <param name="DueDate">The new due date; null to have none.</param>
/// <param name="PriorityLevel">The new level, 1 first; null to have none. A taken level pushes the others down.</param>
/// <param name="Reason">Why; kept in the task's history and sent to the person responsible.</param>
public record RescheduleTaskRequest(
    [DueDateRange] DateOnly? DueDate,
    [Range(1, Services.PriorityLevels.Max)] int? PriorityLevel,
    [Required, StringLength(RescheduleTaskRequest.MaxReasonLength, MinimumLength = 1), SafeText] string Reason)
{
    public const int MaxReasonLength = 1000;
}
