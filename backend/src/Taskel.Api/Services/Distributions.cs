using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>
/// Distributing a task: copies given to several people while the person responsible waits for their answers, the
/// conversation that goes with it, and keeping the distributed task's state in step with its copies.
/// </summary>
/// <remarks>
/// The distributed task waits ("on hold") while any copy is open, with a reason that starts with
/// <see cref="WaitingPrefix"/>; it opens again by itself once every copy is answered. A reason the user wrote
/// themselves is never replaced or cleared.
/// </remarks>
public class Distributions(TaskelDbContext db, TaskNotifier notifier)
{
    public const string WaitingPrefix = "Distribuit către ";

    /// <summary>
    /// Gives a copy of <paramref name="task"/> to each target and puts the task on hold. The copies are created by
    /// the distributor, open, with the task's message, files (shared, not copied on disk) and the given message.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> DistributeAsync(TaskItem task, Guid actorId,
        IReadOnlyList<(Guid? AssigneeId, Guid? GroupId)> targets, string? message, DateOnly? dueDate, CancellationToken ct)
    {
        var copies = new List<TaskItem>();
        foreach (var (assigneeId, groupId) in targets)
        {
            var copy = new TaskItem
            {
                UserId = actorId,
                AssigneeId = assigneeId,
                GroupId = groupId,
                ParentTaskId = task.Id,
                Title = task.Title,
                Description = task.Description,
                Instructions = message,
                DueDate = dueDate ?? task.DueDate,
                Priority = task.Priority,
                Tags = [.. task.Tags]
            };
            foreach (var file in task.Attachments)
            {
                copy.Attachments.Add(new TaskAttachment
                {
                    TaskId = copy.Id,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Size = file.Size,
                    StoredName = file.StoredName,
                    UploadedById = file.UploadedById,
                    UploadedAt = file.UploadedAt
                });
            }
            db.Tasks.Add(copy);
            db.TaskMessages.Add(new TaskMessage
            {
                TaskId = task.Id, RelatedTaskId = copy.Id, AuthorId = actorId, Kind = TaskMessageKind.Distributed, Text = message
            });
            copies.Add(copy);
        }
        await db.SaveChangesAsync(ct);
        await SyncAsync(task, ct);
        await db.SaveChangesAsync(ct);

        foreach (var copy in copies)
            await notifier.TaskReceivedAsync(copy, actorId, TaskNotificationReason.Distributed, message, ct);
        return copies;
    }

    /// <summary>
    /// Records what completing a copy means for the task it came from: the answer in its conversation, and, once
    /// everyone has answered, the task open again. The distributor is told, unless they closed the copy themselves.
    /// </summary>
    public async Task CopyCompletedAsync(TaskItem copy, Guid actorId, string? answer, CancellationToken ct)
    {
        if (copy.ParentTaskId is not { } parentId) return;
        var parent = await db.Tasks.Include(t => t.Distributions).FirstOrDefaultAsync(t => t.Id == parentId, ct);
        if (parent is null) return;

        var byRecipient = actorId != copy.UserId;
        db.TaskMessages.Add(new TaskMessage
        {
            TaskId = parentId, RelatedTaskId = copy.Id, AuthorId = actorId,
            Kind = byRecipient ? TaskMessageKind.Answered : TaskMessageKind.Closed, Text = answer
        });
        var wasWaiting = parent.WaitingReason?.StartsWith(WaitingPrefix, StringComparison.Ordinal) == true;
        await SyncAsync(parent, ct);
        var allAnswered = wasWaiting && parent.WaitingReason is null && !parent.IsCompleted;
        if (allAnswered)
            db.TaskMessages.Add(new TaskMessage { TaskId = parentId, Kind = TaskMessageKind.AllAnswered });
        await db.SaveChangesAsync(ct);

        if (byRecipient)
        {
            await notifier.TellAsync(parent, copy.UserId, actorId,
                allAnswered ? TaskNotificationReason.AllAnswered : TaskNotificationReason.Answered, answer, ct);
        }
    }

    /// <summary>Reopens an answered copy with the distributor's message; the task it came from waits again.</summary>
    public async Task SendBackAsync(TaskItem parent, TaskItem copy, Guid actorId, string message, CancellationToken ct)
    {
        copy.IsCompleted = false;
        copy.CompletedAt = null;
        copy.CompletionResponse = null;
        copy.Instructions = message;
        db.TaskMessages.Add(new TaskMessage
        {
            TaskId = parent.Id, RelatedTaskId = copy.Id, AuthorId = actorId, Kind = TaskMessageKind.SentBack, Text = message
        });
        await db.SaveChangesAsync(ct);
        await SyncAsync(parent, ct);
        await db.SaveChangesAsync(ct);
        await notifier.TaskReceivedAsync(copy, actorId, TaskNotificationReason.SentBack, message, ct);
    }

    /// <summary>Closes the copies nobody answered, when the distributor completes the task without waiting.</summary>
    public async Task CloseOpenCopiesAsync(TaskItem parent, Guid actorId, CancellationToken ct)
    {
        var open = parent.Distributions.Where(c => !c.IsCompleted).ToList();
        foreach (var copy in open)
        {
            copy.IsCompleted = true;
            copy.CompletedAt = DateTime.UtcNow;
            copy.WaitingReason = null;
            copy.WaitingSince = null;
            db.TaskMessages.Add(new TaskMessage
            {
                TaskId = parent.Id, RelatedTaskId = copy.Id, AuthorId = actorId, Kind = TaskMessageKind.Closed
            });
        }
        await db.SaveChangesAsync(ct);
        foreach (var copy in open.Where(c => c.AssigneeId is not null))
            await notifier.TellAsync(parent, copy.AssigneeId!.Value, actorId, TaskNotificationReason.Closed, null, ct);
    }

    /// <summary>
    /// After a copy was reopened or completed some other way: the task it came from waits while any copy is open,
    /// and opens again when none is.
    /// </summary>
    public async Task CopyChangedAsync(TaskItem copy, CancellationToken ct)
    {
        if (copy.ParentTaskId is not { } parentId) return;
        var parent = await db.Tasks.FirstOrDefaultAsync(t => t.Id == parentId, ct);
        if (parent is null) return;
        await SyncAsync(parent, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds a message to a task's conversation. On a copy, it joins the exchange with the distributor, on the task
    /// it came from; on the distributed task, it goes to one copy's exchange, or stays a note on the task. The other
    /// side of an exchange is told by email.
    /// </summary>
    public async Task<TaskMessage> PostAsync(TaskItem task, Guid actorId, string text, TaskItem? toCopy, CancellationToken ct)
    {
        TaskMessage message;
        Guid? tell = null;
        if (toCopy is not null)
        {
            message = new TaskMessage { TaskId = task.Id, RelatedTaskId = toCopy.Id };
            tell = toCopy.AssigneeId;
        }
        else if (task.ParentTaskId is { } parentId)
        {
            message = new TaskMessage { TaskId = parentId, RelatedTaskId = task.Id };
            tell = actorId == task.UserId ? task.AssigneeId : task.UserId;
        }
        else
        {
            message = new TaskMessage { TaskId = task.Id };
        }
        message.AuthorId = actorId;
        message.Kind = TaskMessageKind.Note;
        message.Text = text;
        db.TaskMessages.Add(message);
        await db.SaveChangesAsync(ct);
        await db.Entry(message).Reference(m => m.Author).LoadAsync(ct);
        if (message.RelatedTaskId is not null)
        {
            await db.Entry(message).Reference(m => m.RelatedTask).Query().Include(t => t.Assignee).Include(t => t.Group).LoadAsync(ct);
        }

        if (tell is { } recipient)
        {
            var thread = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == message.TaskId, ct);
            await notifier.TellAsync(thread, recipient, actorId, TaskNotificationReason.Message, text, ct);
        }
        return message;
    }

    /// <summary>
    /// The conversation of a task, oldest first: its own messages, and, for a copy, its exchange with the
    /// distributor.
    /// </summary>
    public async Task<List<TaskMessage>> ConversationAsync(Guid taskId, CancellationToken ct) =>
        await db.TaskMessages.AsNoTracking()
            .Include(m => m.Author)
            .Include(m => m.RelatedTask).ThenInclude(t => t!.Assignee)
            .Include(m => m.RelatedTask).ThenInclude(t => t!.Group)
            .Where(m => m.TaskId == taskId || m.RelatedTaskId == taskId)
            .OrderBy(m => m.At)
            .ToListAsync(ct);

    /// <summary>Puts the task on hold while any of its copies is open, and takes it off hold when none is.</summary>
    private async Task SyncAsync(TaskItem parent, CancellationToken ct)
    {
        if (parent.IsCompleted) return;
        var waitingOnUs = parent.WaitingReason is null || parent.WaitingReason.StartsWith(WaitingPrefix, StringComparison.Ordinal);
        var open = await db.Tasks
            .Where(t => t.ParentTaskId == parent.Id && !t.IsCompleted)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Assignee != null ? (t.Assignee.DisplayName ?? t.Assignee.Email) : t.Group != null ? t.Group.Name : null)
            .ToListAsync(ct);

        if (open.Count > 0 && waitingOnUs)
        {
            var reason = $"{WaitingPrefix}{string.Join(", ", open.Where(n => n is not null))}: aștept răspunsurile.";
            parent.WaitingReason = reason.Length <= 1000 ? reason : reason[..997] + "...";
            parent.WaitingSince ??= DateTime.UnixEpoch.AddTicks((DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10 * 10);
        }
        else if (open.Count == 0 && parent.WaitingReason?.StartsWith(WaitingPrefix, StringComparison.Ordinal) == true)
        {
            parent.WaitingReason = null;
            parent.WaitingSince = null;
        }
    }
}
