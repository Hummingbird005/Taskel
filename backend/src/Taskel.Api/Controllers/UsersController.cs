using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>The people a task can be assigned to: every account, by name and email only.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController(TaskelDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSummary>>> GetAll(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        return Ok(users.Select(UserSummary.From)
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Email, StringComparer.Ordinal));
    }

    /// <summary>
    /// How many open tasks a user already has in a period, so whoever is about to give them
    /// another can be warned. Only the number is returned, never the tasks.
    /// A task occupies its interval, or its due date alone when it has no start date.
    /// </summary>
    /// <param name="excludeTaskId">A task to leave out, when checking for the task being edited.</param>
    [HttpGet("{id:guid}/workload")]
    public async Task<ActionResult<UserWorkloadResponse>> GetWorkload(
        Guid id,
        [FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to,
        [FromQuery] Guid? excludeTaskId,
        CancellationToken ct)
    {
        if (from > to || to!.Value.DayNumber - from!.Value.DayNumber > MaxWorkloadDays)
        {
            ModelState.AddModelError(nameof(to), $"The period must run forwards and cover at most {MaxWorkloadDays} days.");
            return ValidationProblem(ModelState);
        }
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return NotFound();

        var count = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsCompleted && t.DueDate != null && t.Id != excludeTaskId)
            .Where(t => t.AssigneeId == id || (t.AssigneeId == null && t.GroupId == null && t.UserId == id))
            .CountAsync(t => t.DueDate >= from && (t.StartDate ?? t.DueDate) <= to, ct);
        return Ok(new UserWorkloadResponse(count));
    }

    private const int MaxWorkloadDays = 366;
}
