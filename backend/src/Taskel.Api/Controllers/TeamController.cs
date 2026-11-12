using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;
using Taskel.Api.Validation;

namespace Taskel.Api.Controllers;

/// <summary>
/// A department's workload, for its manager. A manager sees the people who share their department;
/// an administrator may look at any department. A person's tasks are the ones they are responsible
/// for: assigned to them, or created by them and not handed to someone else.
/// </summary>
[ApiController]
[Authorize]
[Route("api/team")]
public class TeamController(TaskelDbContext db, CurrentUser currentUser) : ControllerBase
{
    /// <param name="department">The department to show. Managers may only name their own.</param>
    /// <param name="today">The caller's local date, so "overdue" and "due today" match what they see. Defaults to the server's date.</param>
    [HttpGet]
    public async Task<ActionResult<TeamResponse>> Get(
        [FromQuery, StringLength(Models.User.MaxDepartmentLength), SafeText] string? department,
        [FromQuery] DateOnly? today,
        CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        // With no department named, the caller's own is shown (which may be none at all).
        var named = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        if (named is not null && role != UserRole.Admin && !SameDepartment(named, user.Department)) return OtherDepartment();
        var requested = named ?? user.Department;

        var departments = role == UserRole.Admin
            ? await AllDepartmentsAsync(ct)
            : user.Department is null ? [] : new List<string> { user.Department };
        if (requested is null) return Ok(new TeamResponse(null, departments, [], TeamStats.Empty));

        var people = await db.Users.AsNoTracking()
            .Where(u => u.Department != null && u.Department.ToLower() == requested.ToLower())
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => (t.AssigneeId != null && ids.Contains(t.AssigneeId.Value)) || (t.AssigneeId == null && ids.Contains(t.UserId)))
            .Select(t => new TaskFacts(t.AssigneeId ?? t.UserId, t.IsCompleted, t.DueDate, t.Priority, t.CompletedAt))
            .ToListAsync(ct);
        var byPerson = tasks.ToLookup(t => t.ResponsibleId);

        var day = ClampToday(today);
        var now = DateTime.UtcNow;
        var members = people
            .Select(p => new TeamMember(UserSummary.From(p), p.Role, StatsFor(byPerson[p.Id], day, now)))
            .OrderByDescending(m => m.Stats.Open)
            .ThenBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Show the department as it is written on the accounts, not as it was typed in the query.
        var name = people.Select(p => p.Department).FirstOrDefault() ?? requested;
        return Ok(new TeamResponse(name, departments, members, TeamStats.Sum(members.Select(m => m.Stats))));
    }

    /// <summary>The tasks one person is responsible for, in full, for reading.</summary>
    [HttpGet("members/{id:guid}/tasks")]
    public async Task<ActionResult<IEnumerable<TaskResponse>>> GetMemberTasks(Guid id, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        var member = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (member is null) return NotFound();
        if (role != UserRole.Admin && (member.Department is null || !SameDepartment(member.Department, user.Department)))
            return OtherDepartment();

        var tasks = await db.Tasks.AsNoTracking()
            .Include(t => t.Owner)
            .Include(t => t.Assignee)
            .Where(t => t.AssigneeId == id || (t.AssigneeId == null && t.UserId == id))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return Ok(tasks.Select(TaskResponse.From));
    }

    private record TaskFacts(Guid ResponsibleId, bool IsCompleted, DateOnly? DueDate, TaskPriority Priority, DateTime? CompletedAt);

    private static TeamStats StatsFor(IEnumerable<TaskFacts> tasks, DateOnly today, DateTime now)
    {
        var all = tasks.ToList();
        var open = all.Where(t => !t.IsCompleted).ToList();
        return new TeamStats(
            Open: open.Count,
            Overdue: open.Count(t => t.DueDate < today),
            DueToday: open.Count(t => t.DueDate == today),
            DueNext7Days: open.Count(t => t.DueDate > today && t.DueDate <= today.AddDays(7)),
            HighPriorityOpen: open.Count(t => t.Priority == TaskPriority.High),
            CompletedLast7Days: all.Count(t => t.IsCompleted && t.CompletedAt >= now.AddDays(-7)),
            CompletedLast30Days: all.Count(t => t.IsCompleted && t.CompletedAt >= now.AddDays(-30)));
    }

    /// <summary>The caller's date, accepted only within a day of the server's, where time zones can put it.</summary>
    private static DateOnly ClampToday(DateOnly? requested)
    {
        var serverToday = DateOnly.FromDateTime(DateTime.UtcNow);
        return requested is { } day && Math.Abs(day.DayNumber - serverToday.DayNumber) <= 1 ? day : serverToday;
    }

    private async Task<List<string>> AllDepartmentsAsync(CancellationToken ct)
    {
        var names = await db.Users.AsNoTracking()
            .Where(u => u.Department != null)
            .Select(u => u.Department!)
            .Distinct()
            .ToListAsync(ct);
        return names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool SameDepartment(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private ObjectResult NotAManager() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only managers can see team workload.");

    private ObjectResult OtherDepartment() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "You can only see your own department.");
}
