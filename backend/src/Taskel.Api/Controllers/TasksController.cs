using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Attachments;
using Taskel.Api.Email;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

/// <summary>
/// The tasks the signed-in user can reach, and what they may do with each:
/// the creator everything; the assignee edit, complete and pass on; a member of the group a task
/// was given to read it and take it; someone who passed a task on read it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(
    ITaskRepository repository, IEmailSender emailSender, TaskNotifier notifier, AttachmentStore attachments, Distributions distributions,
    PriorityLevels levels)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskResponse>>> GetAll(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var groups = await repository.GetGroupIdsAsync(userId, ct);
        var tasks = await repository.GetAllAsync(userId, ct);
        return Ok(tasks.Select(task => TaskResponse.From(task, AccessOf(task, userId, groups))));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Get(Guid id, CancellationToken ct) =>
        await LoadAsync(id, ct) is { } found ? Ok(TaskResponse.From(found.Task, found.Access)) : NotFound();

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var target = await ResolveTargetAsync(userId, request.AssigneeId, request.GroupId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var task = new TaskItem
        {
            UserId = userId,
            AssigneeId = target.AssigneeId,
            GroupId = target.GroupId,
            Title = request.Title.Trim(),
            Description = NullIfBlank(request.Description),
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Priority = request.Priority,
            PriorityLevel = request.PriorityLevel,
            Tags = TagList.Normalize(request.Tags)
        };
        if (await levels.MakeRoomAsync(task, ct) is { } full) return LevelsFull(task, full);
        task = await repository.AddAsync(task, ct);
        await notifier.TaskReceivedAsync(task, userId, TaskNotificationReason.Assigned, ct: ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task, TaskAccess.Creator));
    }

    /// <summary>Creates a task from an email for the caller, e.g. sent by the Outlook macro.</summary>
    [HttpPost("from-email")]
    public async Task<ActionResult<TaskResponse>> CreateFromEmail(CreateTaskFromEmailRequest request, CancellationToken ct)
    {
        if (request.Recipients is { Count: > 0 })
        {
            ModelState.AddModelError(nameof(request.Recipients), "To give the email to other people, use from-email/distribute.");
            return ValidationProblem(ModelState);
        }
        var task = EmailTaskFactory.Create(User.GetUserId(), request);
        task.Instructions = InstructionsOf(request);
        task = await repository.AddAsync(task, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task, TaskAccess.Creator));
    }

    /// <summary>
    /// Gives an email to one or more people or groups: each gets a task of their own, created by the
    /// caller, with the instructions, and is told by email. With no recipient, the task is the caller's.
    /// When one of them completes their task, the completion message is emailed to the caller.
    /// </summary>
    [HttpPost("from-email/distribute")]
    public async Task<ActionResult<DistributedEmailResponse>> DistributeEmail(CreateTaskFromEmailRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var targets = new List<(Guid? AssigneeId, Guid? GroupId)>();
        foreach (var recipient in request.Recipients ?? [])
        {
            if (!TryParseRecipient(recipient, out var assigneeId, out var groupId))
            {
                ModelState.AddModelError(nameof(request.Recipients), "Each recipient is a user id, or group: and a group id.");
                continue;
            }
            var target = await ResolveTargetAsync(userId, assigneeId, groupId, ct);
            if (!targets.Contains(target)) targets.Add(target);
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (targets.Count == 0) targets.Add((null, null));

        var instructions = InstructionsOf(request);
        var ids = new List<Guid>();
        foreach (var (assigneeId, groupId) in targets)
        {
            var task = EmailTaskFactory.Create(userId, request);
            task.AssigneeId = assigneeId;
            task.GroupId = groupId;
            task.Instructions = instructions;
            task = await repository.AddAsync(task, ct);
            await notifier.TaskReceivedAsync(task, userId, TaskNotificationReason.Assigned, instructions, ct);
            ids.Add(task.Id);
        }
        return StatusCode(StatusCodes.Status201Created, new DistributedEmailResponse(ids));
    }

    internal static bool TryParseRecipient(string? value, out Guid? assigneeId, out Guid? groupId)
    {
        assigneeId = null;
        groupId = null;
        var text = value?.Trim() ?? string.Empty;
        if (text.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(text[GroupPrefix.Length..], out var group)) return false;
            groupId = group;
            return true;
        }
        if (!Guid.TryParse(text, out var user)) return false;
        assigneeId = user;
        return true;
    }

    private const string GroupPrefix = "group:";

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);

        if (request.IsCompleted && !task.IsCompleted && task.RequiresCompletionReply)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "This task came from an email. Complete it with a response, which is sent to the sender.");
        }
        if (request.IsCompleted && !task.IsCompleted && access == TaskAccess.Assignee && task.PendingReturn() is not null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "This task goes back to whoever forwarded it when it is completed. Complete it with complete.");
        }
        if (request.IsCompleted && !task.IsCompleted && (task.ParentTaskId is not null || task.Distributions.Count > 0))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: task.ParentTaskId is not null
                ? "This task was distributed to you: complete it with complete and an answer, which goes to whoever distributed it."
                : "This task was distributed: complete it with complete.");
        }
        var reopened = !request.IsCompleted && task.IsCompleted;

        task.Title = request.Title.Trim();
        task.Description = NullIfBlank(request.Description);
        if (request.IsCompleted != task.IsCompleted)
        {
            task.IsCompleted = request.IsCompleted;
            task.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
            // Finished is no longer on hold; reopened starts as open, not as it was before.
            ClearWaiting(task);
        }
        task.StartDate = request.StartDate;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.PriorityLevel = request.PriorityLevel;
        task.Tags = TagList.Normalize(request.Tags);
        if (await levels.MakeRoomAsync(task, ct) is { } full) return LevelsFull(task, full);
        await repository.SaveAsync(task, ct);
        // A reopened copy is open work again: the task it came from waits for it.
        if (reopened) await distributions.CopyChangedAsync(task, ct);
        return Ok(TaskResponse.From(task, access));
    }

    /// <summary>
    /// Assigns the task to a user or a group, or back to its creator. Only the creator may do this;
    /// someone who received the task passes it on with <see cref="Forward"/> instead.
    /// </summary>
    [HttpPut("{id:guid}/assignee")]
    public async Task<ActionResult<TaskResponse>> Assign(Guid id, AssignTaskRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (access != TaskAccess.Creator) return CreatorOnly("reassign");

        var target = await ResolveTargetAsync(task.UserId, request.AssigneeId, request.GroupId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var changed = task.AssigneeId != target.AssigneeId || task.GroupId != target.GroupId;
        task.AssigneeId = target.AssigneeId;
        task.GroupId = target.GroupId;
        // On someone else's day, the task keeps its level only if there is room for it.
        await levels.FitOrDropAsync(task, ct);
        // The creator takes the task's course into their own hands: nobody is waiting for it back any more.
        if (changed)
        {
            foreach (var forward in task.Forwards) forward.ReturnPending = false;
        }
        await repository.SaveAsync(task, ct);
        // Saving it again with the same person is not news to them.
        if (changed) await notifier.TaskReceivedAsync(task, task.UserId, TaskNotificationReason.Assigned, ct: ct);
        return Ok(TaskResponse.From(task, access));
    }

    /// <summary>
    /// Passes a received task on to another user or to a group. Whoever passes it on can still read
    /// it afterwards, and the step is kept in the task's history.
    /// </summary>
    [HttpPost("{id:guid}/forward")]
    public async Task<ActionResult<TaskResponse>> Forward(Guid id, ForwardTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (access != TaskAccess.Assignee)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: access == TaskAccess.Creator
                ? "You created this task: assign it instead of forwarding it."
                : "Only the person the task is assigned to can forward it.");
        }

        if (request.AssigneeId is null == request.GroupId is null)
            ModelState.AddModelError(nameof(request.AssigneeId), "Choose one user or one group to forward the task to.");
        else if (request.AssigneeId == userId)
            ModelState.AddModelError(nameof(request.AssigneeId), "The task is already yours.");
        var target = await ResolveTargetAsync(task.UserId, request.AssigneeId, request.GroupId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        task.Forwards.Add(new TaskForward
        {
            TaskId = task.Id,
            FromUserId = userId,
            ToUserId = request.AssigneeId,
            ToGroupId = request.GroupId,
            Note = NullIfBlank(request.Note),
            ReturnPending = request.ReturnToMe
        });
        task.AssigneeId = target.AssigneeId;
        task.GroupId = target.GroupId;
        // On someone else's day, the task keeps its level only if there is room for it.
        await levels.FitOrDropAsync(task, ct);
        await repository.SaveAsync(task, ct);
        await notifier.TaskReceivedAsync(task, userId, TaskNotificationReason.Forwarded, NullIfBlank(request.Note), ct);
        return Ok(TaskResponse.From(task, AccessOf(task, userId, await repository.GetGroupIdsAsync(userId, ct))));
    }

    /// <summary>Takes a task that was given to one of the user's groups: it becomes theirs.</summary>
    [HttpPost("{id:guid}/claim")]
    public async Task<ActionResult<TaskResponse>> Claim(Guid id, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (access != TaskAccess.Group)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This task is not waiting to be taken by one of your groups.");
        }

        task.AssigneeId = User.GetUserId();
        await levels.FitOrDropAsync(task, ct);
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task, TaskAccess.Assignee));
    }

    /// <summary>Hands a task taken from a group back to that group.</summary>
    [HttpPost("{id:guid}/release")]
    public async Task<ActionResult<TaskResponse>> Release(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (access != TaskAccess.Assignee || task.GroupId is null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Only a task you took from a group can be handed back to it.");
        }

        task.AssigneeId = null;
        await levels.FitOrDropAsync(task, ct);
        await repository.SaveAsync(task, ct);
        var groups = await repository.GetGroupIdsAsync(userId, ct);
        return Ok(TaskResponse.From(task, AccessOf(task, userId, groups)));
    }

    /// <summary>
    /// Completes a task. When someone who forwarded it asked for it back, it goes back to them instead,
    /// with the message, and stays open. For a task created from an email the message is required and
    /// is emailed (see <see cref="TaskItem.ReplyAddressFor"/>); if the email cannot be sent, the task stays open.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<TaskResponse>> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");

        var response = NullIfBlank(request.Response);
        var openCopies = task.Distributions.Count(c => !c.IsCompleted);
        if (openCopies > 0 && !request.CloseDistributions)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: openCopies == 1
                ? "1 person has not answered yet. Complete with closeDistributions to close their task too."
                : $"{openCopies} people have not answered yet. Complete with closeDistributions to close their tasks too.");
        }
        if (task.ParentTaskId is not null && access == TaskAccess.Assignee && response is null)
        {
            ModelState.AddModelError(nameof(request.Response),
                $"An answer is required: it goes to {task.Owner?.DisplayName ?? "whoever distributed the task"}.");
            return ValidationProblem(ModelState);
        }
        var answerFiles = request.AttachmentIds ?? [];
        if (answerFiles.Any(fileId => task.Attachments.All(a => a.Id != fileId)))
        {
            ModelState.AddModelError(nameof(request.AttachmentIds), "Only files of this task can be attached to its answer.");
            return ValidationProblem(ModelState);
        }
        foreach (var file in task.Attachments.Where(a => answerFiles.Contains(a.Id))) file.WithAnswer = true;
        var pending = access == TaskAccess.Assignee ? task.PendingReturn() : null;
        var replyTo = pending is null ? task.ReplyAddressFor(access) : null;
        if (task.RequiresCompletionReply && response is null)
        {
            ModelState.AddModelError(nameof(request.Response), replyTo is null
                ? "A message is required: it goes back with the task."
                : $"A response is required. It is sent to {replyTo}.");
            return ValidationProblem(ModelState);
        }

        if (pending is not null) return await ReturnAsync(task, pending, userId, response, ct);

        if (replyTo is not null && await SendCompletionReplyAsync(task, access, replyTo, response!, answerFiles, ct) is { } failure)
            return failure;

        if (openCopies > 0) await distributions.CloseOpenCopiesAsync(task, userId, ct);
        task.IsCompleted = true;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletionResponse = response;
        ClearWaiting(task);
        foreach (var forward in task.Forwards) forward.ReturnPending = false;
        await repository.SaveAsync(task, ct);
        // A copy's answer goes to the task it was distributed from.
        await distributions.CopyCompletedAsync(task, userId, response, ct);
        return Ok(TaskResponse.From(task, access));
    }

    /// <summary>
    /// Sends an answer without completing the task, to where completing it would send one: by email for a task that
    /// came from an email, to whoever distributed a copy, to whoever forwarded it and asked for it back, or to whoever
    /// gave it. The answer stays in the task's conversation, with the files chosen; the task stays open.
    /// </summary>
    [HttpPost("{id:guid}/reply")]
    public async Task<ActionResult<TaskMessageResponse>> Reply(Guid id, ReplyTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");
        var fileIds = request.AttachmentIds ?? [];
        if (fileIds.Any(fileId => task.Attachments.All(a => a.Id != fileId)))
        {
            ModelState.AddModelError(nameof(request.AttachmentIds), "Only files of this task can be attached to its answer.");
            return ValidationProblem(ModelState);
        }
        var text = request.Response.Trim().Replace("\r\n", "\n");

        TaskMessage message;
        if (task.ParentTaskId is not null && access == TaskAccess.Assignee)
        {
            message = await distributions.CopyRepliedAsync(task, userId, text, fileIds, ct);
        }
        else
        {
            var pending = access == TaskAccess.Assignee ? task.PendingReturn() : null;
            var replyTo = pending is null ? task.ReplyAddressFor(access) : null;
            Guid? tell = pending?.FromUserId ?? (replyTo is null && access == TaskAccess.Assignee ? task.UserId : null);
            if (replyTo is null && tell is null)
            {
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "There is no one to answer: this task is yours and did not come from an email.");
            }
            if (replyTo is not null
                && await SendCompletionReplyAsync(task, access, replyTo, text, fileIds, ct, completed: false) is { } failure)
                return failure;

            message = await distributions.RepliedAsync(task, userId, text, fileIds, replyTo, tell, ct);
        }
        var files = await distributions.AnswerFilesAsync(task.Id, ct);
        return Ok(TaskMessageResponse.From(message, files[message.Id]));
    }

    /// <summary>Sends the task back to whoever forwarded it and asked for it back, with the resolution.</summary>
    private async Task<ActionResult<TaskResponse>> ReturnAsync(
        TaskItem task, TaskForward pending, Guid userId, string? response, CancellationToken ct)
    {
        pending.ReturnPending = false;
        task.Forwards.Add(new TaskForward
        {
            TaskId = task.Id,
            FromUserId = userId,
            ToUserId = pending.FromUserId,
            Note = response,
            IsReturn = true
        });
        task.AssigneeId = pending.FromUserId == task.UserId ? null : pending.FromUserId;
        task.GroupId = null;
        await levels.FitOrDropAsync(task, ct);
        ClearWaiting(task);
        await repository.SaveAsync(task, ct);
        await notifier.TaskReceivedAsync(task, userId, TaskNotificationReason.Returned, response, ct);
        return Ok(TaskResponse.From(task, AccessOf(task, userId, await repository.GetGroupIdsAsync(userId, ct))));
    }

    /// <summary>The most an email reply carries in files; beyond it, files are named and left in Taskel.</summary>
    public const long MaxReplyAttachmentBytes = 20L * 1024 * 1024;

    /// <summary>
    /// Emails the completion message of an email task, as a reply in the original email's thread (see
    /// <see cref="ReplyComposer"/>). To the task's creator, a colleague, it carries the history in Taskel and every
    /// file sent with an answer; to the email's sender, only the answer, the files attached to it now and their own
    /// email quoted. Null when it was sent; otherwise the answer to give.
    /// </summary>
    private async Task<ObjectResult?> SendCompletionReplyAsync(
        TaskItem task, TaskAccess access, string to, string response, IReadOnlyCollection<Guid> answerFiles, CancellationToken ct,
        bool completed = true)
    {
        var internalReply = access != TaskAccess.Creator;
        var closedBy = (internalReply ? task.Assignee : task.Owner) ?? throw new InvalidOperationException("Task people are not loaded.");
        var messages = internalReply ? await distributions.ConversationAsync(task.Id, ct) : [];

        var candidates = task.Attachments
            // An answer sent while the task stays open carries only the files chosen for it.
            .Where(a => internalReply && completed ? a.WithAnswer : answerFiles.Contains(a.Id))
            .OrderBy(a => a.UploadedAt)
            .ToList();
        var sent = new List<TaskAttachment>();
        var tooLarge = new List<string>();
        long total = 0;
        foreach (var file in candidates)
        {
            if (total + file.Size > MaxReplyAttachmentBytes) tooLarge.Add(file.FileName);
            else
            {
                sent.Add(file);
                total += file.Size;
            }
        }

        var (text, html) = ReplyComposer.Compose(task, closedBy, response, messages, internalReply,
            [.. sent.Select(f => f.FileName)], tooLarge, completed);
        var files = sent.Select(f => new ReplyAttachment(f.FileName, f.ContentType, () => attachments.Open(f.StoredName))).ToList();
        try
        {
            await emailSender.SendReplyAsync(
                new ReplyEmail(to, task.SourceEmailSubject, text, task.SourceEmailMessageId, html, files,
                    new Sender(string.IsNullOrWhiteSpace(closedBy.DisplayName) ? closedBy.Email : closedBy.DisplayName, closedBy.Email)), ct);
            return null;
        }
        catch (EmailNotConfiguredException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Email sending is not set up on the Taskel server, so the reply cannot be sent."
                       + (completed ? " The task was not completed." : ""));
        }
        catch (EmailSendException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The reply email could not be sent." + (completed ? " The task was not completed." : "") + " Try again.");
        }
    }

    /// <summary>
    /// Puts an open task on hold, with the reason it waits. Calling it again on a task already on hold
    /// replaces the reason and keeps the date it started waiting.
    /// </summary>
    [HttpPost("{id:guid}/wait")]
    public async Task<ActionResult<TaskResponse>> Wait(Guid id, WaitTaskRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "A completed task cannot be put on hold. Reopen it first.");

        var reason = NullIfBlank(request.Reason);
        if (reason is null)
        {
            ModelState.AddModelError(nameof(request.Reason), "Say what the task is waiting for.");
            return ValidationProblem(ModelState);
        }

        task.WaitingReason = reason;
        // Stored to the microsecond, PostgreSQL's precision, so the answer matches what is read back later.
        task.WaitingSince ??= DateTime.UnixEpoch.AddTicks((DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10 * 10);
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task, access));
    }

    /// <summary>Takes a task off hold: it is open again.</summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<TaskResponse>> Resume(Guid id, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);
        if (task.State != TaskState.Waiting)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is not on hold.");

        ClearWaiting(task);
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task, access));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        if (found.Value.Access != TaskAccess.Creator) return CreatorOnly("delete");

        // The rows go with the task; the files are removed once the task is really gone, unless a distributed copy
        // or the task it came from still lists them.
        var task = found.Value.Task;
        var files = task.Attachments.Select(a => a.StoredName).ToList();
        await repository.DeleteAsync(task, ct);
        foreach (var file in files)
        {
            if (!await repository.IsFileSharedAsync(file, ct)) attachments.Delete(file);
        }
        // A deleted copy no longer holds up the task it came from.
        await distributions.CopyChangedAsync(task, ct);
        return NoContent();
    }

    /// <summary>The task, if the user can reach it, with what they may do with it.</summary>
    private async Task<(TaskItem Task, TaskAccess Access)?> LoadAsync(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await repository.GetAsync(userId, id, ct);
        if (task is null) return null;
        return (task, AccessOf(task, userId, await repository.GetGroupIdsAsync(userId, ct)));
    }

    /// <summary>What a user who can reach a task may do with it. The strongest reason wins.</summary>
    public static TaskAccess AccessOf(TaskItem task, Guid userId, IReadOnlySet<Guid> userGroups)
    {
        if (task.UserId == userId) return TaskAccess.Creator;
        if (task.AssigneeId == userId) return TaskAccess.Assignee;
        if (task.AssigneeId is null && task.GroupId is { } group && userGroups.Contains(group)) return TaskAccess.Group;
        return TaskAccess.Viewer;
    }

    private static void ClearWaiting(TaskItem task)
    {
        task.WaitingReason = null;
        task.WaitingSince = null;
    }

    private static bool CanWork(TaskAccess access) => access is TaskAccess.Creator or TaskAccess.Assignee;

    private ObjectResult ReadOnly(TaskAccess access) =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: access == TaskAccess.Group
            ? "Take this task first: it is still waiting for someone in the group."
            : "You can read this task but not change it.");

    /// <summary>
    /// Who a task should go to, as stored: a user, a group, or neither when it stays with its creator
    /// (nothing chosen, or the creator named). Adds model errors for a target that does not exist
    /// or for a user and a group named together.
    /// </summary>
    private async Task<(Guid? AssigneeId, Guid? GroupId)> ResolveTargetAsync(
        Guid creatorId, Guid? assigneeId, Guid? groupId, CancellationToken ct)
    {
        if (assigneeId is not null && groupId is not null)
        {
            ModelState.AddModelError(nameof(AssignTaskRequest.GroupId), "Choose a user or a group, not both.");
            return (null, null);
        }
        if (groupId is not null)
        {
            if (!await repository.GroupExistsAsync(groupId.Value, ct))
                ModelState.AddModelError(nameof(AssignTaskRequest.GroupId), "The group does not exist.");
            return (null, groupId);
        }
        if (assigneeId is null || assigneeId == creatorId) return (null, null);
        if (!await repository.UserExistsAsync(assigneeId.Value, ct))
            ModelState.AddModelError(nameof(AssignTaskRequest.AssigneeId), "The user to assign the task to does not exist.");
        return (assigneeId, null);
    }

    /// <summary>The level asked for would push a task on that day past the last level.</summary>
    private ObjectResult LevelsFull(TaskItem task, DateOnly day) =>
        Problem(statusCode: StatusCodes.Status409Conflict,
            title: $"Priority levels {task.PriorityLevel} to {PriorityLevels.Max} are all taken on {day:yyyy-MM-dd}: choose another level.");

    private ObjectResult CreatorOnly(string action) =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Only the person who created this task can {action} it.");

    /// <summary>The instructions with Unix line endings (Outlook sends Windows ones), or null when there are none.</summary>
    private static string? InstructionsOf(CreateTaskFromEmailRequest request) =>
        NullIfBlank(request.Instructions?.Replace("\r\n", "\n").Replace('\r', '\n'));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
