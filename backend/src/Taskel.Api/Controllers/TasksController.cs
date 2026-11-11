using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repository) : ControllerBase
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

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.IsCompleted = request.IsCompleted;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.Tags = TagList.Normalize(request.Tags);
        await repository.UpdateAsync(task, ct);
        return Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await repository.DeleteAsync(User.GetUserId(), id, ct) ? NoContent() : NotFound();
}
