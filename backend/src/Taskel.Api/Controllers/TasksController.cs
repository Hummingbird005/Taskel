using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Email;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repository, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItem>>> GetAll(CancellationToken ct) =>
        Ok(await repository.GetAllAsync(User.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskItem>> Get(Guid id, CancellationToken ct) =>
        await repository.GetAsync(User.GetUserId(), id, ct) is { } task ? Ok(task) : NotFound();

    [HttpPost]
    public async Task<ActionResult<TaskItem>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var task = await repository.AddAsync(new TaskItem
        {
            UserId = User.GetUserId(),
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            DueDate = request.DueDate,
            Priority = request.Priority,
            Tags = TagList.Normalize(request.Tags)
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    /// <summary>Creates a task from an email, e.g. sent by the Outlook macro.</summary>
    [HttpPost("from-email")]
    public async Task<ActionResult<TaskItem>> CreateFromEmail(CreateTaskFromEmailRequest request, CancellationToken ct)
    {
        var task = await repository.AddAsync(EmailTaskFactory.Create(User.GetUserId(), request), ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskItem>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await repository.GetAsync(User.GetUserId(), id, ct);
        if (task is null) return NotFound();

        if (request.IsCompleted && !task.IsCompleted && task.RequiresCompletionReply)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "This task came from an email. Complete it with a response, which is sent to the sender.");
        }

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        if (request.IsCompleted != task.IsCompleted)
        {
            task.IsCompleted = request.IsCompleted;
            task.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
        }
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.Tags = TagList.Normalize(request.Tags);
        await repository.UpdateAsync(task, ct);
        return Ok(task);
    }

    /// <summary>
    /// Marks a task as completed. For a task created from an email the response is required and is
    /// emailed to the sender as a reply; if the email cannot be sent, the task stays open.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<TaskItem>> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct)
    {
        var task = await repository.GetAsync(User.GetUserId(), id, ct);
        if (task is null) return NotFound();
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");

        var response = string.IsNullOrWhiteSpace(request.Response) ? null : request.Response.Trim();

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
        await repository.UpdateAsync(task, ct);
        return Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await repository.DeleteAsync(User.GetUserId(), id, ct) ? NoContent() : NotFound();
}
