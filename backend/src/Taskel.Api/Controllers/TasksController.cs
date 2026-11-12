using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Email;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

/// <summary>
/// Tasks the signed-in user created or was assigned. Both the creator and the assignee can read,
/// edit and complete a task; only the creator can delete or reassign it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repository, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskResponse>>> GetAll(CancellationToken ct) =>
        Ok((await repository.GetAllAsync(User.GetUserId(), ct)).Select(TaskResponse.From));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Get(Guid id, CancellationToken ct) =>
        await repository.GetAsync(User.GetUserId(), id, ct) is { } task ? Ok(TaskResponse.From(task)) : NotFound();

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var assigneeId = await ResolveAssigneeAsync(userId, request.AssigneeId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var task = await repository.AddAsync(new TaskItem
        {
            UserId = userId,
            AssigneeId = assigneeId,
            Title = request.Title.Trim(),
            Description = NullIfBlank(request.Description),
            DueDate = request.DueDate,
            Priority = request.Priority,
            Tags = TagList.Normalize(request.Tags)
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task));
    }

    /// <summary>Creates a task from an email, e.g. sent by the Outlook macro.</summary>
    [HttpPost("from-email")]
    public async Task<ActionResult<TaskResponse>> CreateFromEmail(CreateTaskFromEmailRequest request, CancellationToken ct)
    {
        var task = await repository.AddAsync(EmailTaskFactory.Create(User.GetUserId(), request), ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, TaskResponse.From(task));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await repository.GetAsync(User.GetUserId(), id, ct);
        if (task is null) return NotFound();

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
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.Tags = TagList.Normalize(request.Tags);
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task));
    }

    /// <summary>Assigns the task to another user, or back to its creator. Only the creator may do this.</summary>
    [HttpPut("{id:guid}/assignee")]
    public async Task<ActionResult<TaskResponse>> Assign(Guid id, AssignTaskRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await repository.GetAsync(userId, id, ct);
        if (task is null) return NotFound();
        if (task.UserId != userId) return CreatorOnly("reassign");

        var assigneeId = await ResolveAssigneeAsync(userId, request.AssigneeId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        task.AssigneeId = assigneeId;
        await repository.SaveAsync(task, ct);
        return Ok(TaskResponse.From(task));
    }

    /// <summary>
    /// Marks a task as completed. For a task created from an email the response is required and is
    /// emailed to the sender as a reply; if the email cannot be sent, the task stays open.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<TaskResponse>> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct)
    {
        var task = await repository.GetAsync(User.GetUserId(), id, ct);
        if (task is null) return NotFound();
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
        return Ok(TaskResponse.From(task));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await repository.GetAsync(userId, id, ct);
        if (task is null) return NotFound();
        if (task.UserId != userId) return CreatorOnly("delete");

        await repository.DeleteAsync(task, ct);
        return NoContent();
    }

    /// <summary>
    /// The assignee to store: null when the task stays with its creator (no assignee given, or the
    /// creator named themselves). Adds a model error when the user does not exist.
    /// </summary>
    private async Task<Guid?> ResolveAssigneeAsync(Guid creatorId, Guid? requested, CancellationToken ct)
    {
        if (requested is null || requested == creatorId) return null;
        if (!await repository.UserExistsAsync(requested.Value, ct))
            ModelState.AddModelError(nameof(AssignTaskRequest.AssigneeId), "The user to assign the task to does not exist.");
        return requested;
    }

    private ObjectResult CreatorOnly(string action) =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Only the person who created this task can {action} it.");

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
