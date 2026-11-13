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
public class TasksController(ITaskRepository repository, IEmailSender emailSender, TaskNotifier notifier, AttachmentStore attachments)
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

        var task = await repository.AddAsync(new TaskItem
        {
            UserId = userId,
            AssigneeId = target.AssigneeId,
            GroupId = target.GroupId,
            Title = request.Title.Trim(),
            Description = NullIfBlank(request.Description),
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Priority = request.Priority,
            Tags = TagList.Normalize(request.Tags)
        }, ct);
        await notifier.TaskReceivedAsync(task, userId, TaskNotificationReason.Assigned, ct: ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task, TaskAccess.Creator));
    }

    /// <summary>Creates a task from an email, e.g. sent by the Outlook macro.</summary>
    [HttpPost("from-email")]
    public async Task<ActionResult<TaskResponse>> CreateFromEmail(CreateTaskFromEmailRequest request, CancellationToken ct)
    {
        var task = await repository.AddAsync(EmailTaskFactory.Create(User.GetUserId(), request), ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task, TaskAccess.Creator));
    }

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

        task.Title = request.Title.Trim();
        task.Description = NullIfBlank(request.Description);
        if (request.IsCompleted != task.IsCompleted)
        {
            task.IsCompleted = request.IsCompleted;
            task.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
        }
        task.StartDate = request.StartDate;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.Tags = TagList.Normalize(request.Tags);
        await repository.SaveAsync(task, ct);
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
            Note = NullIfBlank(request.Note)
        });
        task.AssigneeId = target.AssigneeId;
        task.GroupId = target.GroupId;
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
        await repository.SaveAsync(task, ct);
        var groups = await repository.GetGroupIdsAsync(userId, ct);
        return Ok(TaskResponse.From(task, AccessOf(task, userId, groups)));
    }

    /// <summary>
    /// Marks a task as completed. For a task created from an email the response is required and is
    /// emailed to the sender as a reply; if the email cannot be sent, the task stays open.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<TaskResponse>> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!CanWork(access)) return ReadOnly(access);
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");

        var response = NullIfBlank(request.Response);

        if (task.RequiresCompletionReply)
        {
            if (response is null)
            {
                ModelState.AddModelError(nameof(request.Response),
                    $"A response is required. It is sent to {task.SourceEmailAddress}.");
                return ValidationProblem(ModelState);
            }

            try
            {
                await emailSender.SendReplyAsync(
                    new ReplyEmail(task.SourceEmailAddress!, task.SourceEmailSubject, response, task.SourceEmailMessageId), ct);
            }
            catch (EmailNotConfiguredException)
            {
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Email sending is not set up on the Taskel server, so the reply cannot be sent. The task was not completed.");
            }
            catch (EmailSendException)
            {
                return Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "The reply email could not be sent. The task was not completed. Try again.");
            }
        }

        task.IsCompleted = true;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletionResponse = response;
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task, access));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        if (found.Value.Access != TaskAccess.Creator) return CreatorOnly("delete");

        // The rows go with the task; the files are removed once the task is really gone.
        var files = found.Value.Task.Attachments.Select(a => a.StoredName).ToList();
        await repository.DeleteAsync(found.Value.Task, ct);
        foreach (var file in files) attachments.Delete(file);
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

    private ObjectResult CreatorOnly(string action) =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Only the person who created this task can {action} it.");

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
