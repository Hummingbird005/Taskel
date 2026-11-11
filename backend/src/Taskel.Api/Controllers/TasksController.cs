using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<TaskItem>> GetAll() => Ok(repository.GetAll());

    [HttpGet("{id:guid}")]
    public ActionResult<TaskItem> Get(Guid id) =>
        repository.Get(id) is { } task ? Ok(task) : NotFound();

    [HttpPost]
    public ActionResult<TaskItem> Create(CreateTaskRequest request)
    {
        var task = repository.Add(new TaskItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim()
        });
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    [HttpPut("{id:guid}")]
    public ActionResult<TaskItem> Update(Guid id, UpdateTaskRequest request)
    {
        var task = repository.Get(id);
        if (task is null) return NotFound();

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.IsCompleted = request.IsCompleted;
        repository.Update(task);
        return Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) => repository.Delete(id) ? NoContent() : NotFound();
}
