using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItem>>> GetAll(CancellationToken ct) =>
        Ok(await repository.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskItem>> Get(Guid id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) is { } task ? Ok(task) : NotFound();

    [HttpPost]
    public async Task<ActionResult<TaskItem>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var task = await repository.AddAsync(new TaskItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim()
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskItem>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await repository.GetAsync(id, ct);
        if (task is null) return NotFound();

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.IsCompleted = request.IsCompleted;
        await repository.UpdateAsync(task, ct);
        return Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await repository.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
