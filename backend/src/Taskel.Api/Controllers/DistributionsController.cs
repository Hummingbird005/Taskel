using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

/// <summary>
/// Distributing a task to several people, sending one person's copy back, and the conversation of a task.
/// See <see cref="Distributions"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tasks")]
public class DistributionsController(ITaskRepository repository, Distributions distributions) : ControllerBase
{
    /// <summary>
    /// Gives a copy of the task to each recipient. Only the person responsible for the task may: its assignee, or
    /// its creator while it is with nobody else. The task waits for their answers.
    /// </summary>
    [HttpPost("{id:guid}/distribute")]
    public async Task<ActionResult<TaskResponse>> Distribute(Guid id, DistributeTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!IsResponsible(task, access))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the person responsible for this task can distribute it.");
        }
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "A completed task cannot be distributed. Reopen it first.");

        var targets = new List<(Guid? AssigneeId, Guid? GroupId)>();
        foreach (var recipient in request.Recipients)
        {
            if (!TasksController.TryParseRecipient(recipient, out var assigneeId, out var groupId))
            {
                ModelState.AddModelError(nameof(request.Recipients), "Each recipient is a user id, or group: and a group id.");
                continue;
            }
            if (assigneeId == userId)
            {
                ModelState.AddModelError(nameof(request.Recipients), "You cannot distribute a task to yourself.");
                continue;
            }
            if (assigneeId is { } user && !await repository.UserExistsAsync(user, ct))
                ModelState.AddModelError(nameof(request.Recipients), "A recipient does not exist.");
            if (groupId is { } group && !await repository.GroupExistsAsync(group, ct))
                ModelState.AddModelError(nameof(request.Recipients), "A group does not exist.");
            if (task.Distributions.Any(c => !c.IsCompleted && c.AssigneeId == assigneeId && c.GroupId == groupId))
            {
                ModelState.AddModelError(nameof(request.Recipients),
                    "A recipient already has an open copy of this task. Send them a message instead.");
            }
            if (!targets.Contains((assigneeId, groupId))) targets.Add((assigneeId, groupId));
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim().Replace("\r\n", "\n");
        await distributions.DistributeAsync(task, userId, targets, message, request.DueDate, ct);
        return await ReloadAsync(id, ct);
    }

    /// <summary>Reopens one answered copy with a message: the answer was not enough.</summary>
    [HttpPost("{id:guid}/distributions/{copyId:guid}/send-back")]
    public async Task<ActionResult<TaskResponse>> SendBack(Guid id, Guid copyId, SendBackRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (!IsResponsible(task, access))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the person responsible for this task can send an answer back.");
        }
        var copy = task.Distributions.FirstOrDefault(c => c.Id == copyId);
        if (copy is null) return NotFound();
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is completed. Reopen it first.");
        if (!copy.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This person has not answered yet.");

        await distributions.SendBackAsync(task, copy, User.GetUserId(), request.Message.Trim().Replace("\r\n", "\n"), ct);
        return await ReloadAsync(id, ct);
    }

    /// <summary>
    /// The task's conversation, oldest first. A distributed task shows every exchange with its recipients; a copy,
    /// its own exchange with the distributor.
    /// </summary>
    [HttpGet("{id:guid}/conversation")]
    public async Task<ActionResult<IEnumerable<TaskMessageResponse>>> Conversation(Guid id, CancellationToken ct)
    {
        if (await LoadAsync(id, ct) is null) return NotFound();
        var messages = await distributions.ConversationAsync(id, ct);
        return Ok(messages.Select(TaskMessageResponse.From));
    }

    /// <summary>Writes in the task's conversation. Only those who work on the task may.</summary>
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<TaskMessageResponse>> Post(Guid id, PostMessageRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(id, ct);
        if (found is null) return NotFound();
        var (task, access) = found.Value;
        if (access is not (TaskAccess.Creator or TaskAccess.Assignee))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the people who work on this task can write in its conversation.");
        }

        TaskItem? copy = null;
        if (request.RelatedTaskId is { } copyId)
        {
            copy = task.Distributions.FirstOrDefault(c => c.Id == copyId);
            if (copy is null)
            {
                ModelState.AddModelError(nameof(request.RelatedTaskId), "That is not a copy of this task.");
                return ValidationProblem(ModelState);
            }
        }

        var message = await distributions.PostAsync(task, User.GetUserId(), request.Text.Trim().Replace("\r\n", "\n"), copy, ct);
        return StatusCode(StatusCodes.Status201Created, TaskMessageResponse.From(message));
    }

    /// <summary>Who answers for the task now: its assignee, or its creator while it is with nobody else.</summary>
    private static bool IsResponsible(TaskItem task, TaskAccess access) =>
        access == TaskAccess.Assignee || (access == TaskAccess.Creator && task.AssigneeId is null && task.GroupId is null);

    private async Task<(TaskItem Task, TaskAccess Access)?> LoadAsync(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await repository.GetAsync(userId, id, ct);
        if (task is null) return null;
        return (task, TasksController.AccessOf(task, userId, await repository.GetGroupIdsAsync(userId, ct)));
    }

    private async Task<ActionResult<TaskResponse>> ReloadAsync(Guid id, CancellationToken ct) =>
        await LoadAsync(id, ct) is { } found ? Ok(TaskResponse.From(found.Task, found.Access)) : NotFound();
}
