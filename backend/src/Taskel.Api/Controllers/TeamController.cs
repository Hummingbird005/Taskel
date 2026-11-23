using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Attachments;
using Taskel.Api.Data;
using Taskel.Api.Email;
using Taskel.Api.Models;
using Taskel.Api.Services;
using Taskel.Api.Validation;

namespace Taskel.Api.Controllers;

/// <summary>
/// A department's workload, for its manager. A manager sees the people who share their department;
/// an administrator may look at any department. A person's tasks are the ones they are responsible
/// for: assigned to them, or created by them and not handed to someone else or to a group.
/// </summary>
[ApiController]
[Authorize]
[Route("api/team")]
public class TeamController(
    TaskelDbContext db, CurrentUser currentUser, AttachmentStore attachments, TaskNotifier notifier, PriorityLevels levels,
    ILogger<TeamController> logger)
    : ControllerBase
{
    /// <summary>How long a completed task stays in the team's list.</summary>
    private static readonly TimeSpan RecentlyCompleted = TimeSpan.FromDays(14);

    /// <summary>
    /// Every task the people of a department are responsible for: the open ones, and those completed in the last
    /// two weeks. For reading, and for moving tasks (<see cref="Reschedule"/>).
    /// </summary>
    /// <param name="department">The department to show. Managers may only name their own.</param>
    [HttpGet("tasks")]
    public async Task<ActionResult<IEnumerable<TaskResponse>>> GetTeamTasks(
        [FromQuery, StringLength(Models.User.MaxDepartmentLength), SafeText] string? department, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        var named = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        if (named is not null && role != UserRole.Admin && !SameDepartment(named, user.Department)) return OtherDepartment();
        var requested = named ?? user.Department;
        if (requested is null) return Ok(Array.Empty<TaskResponse>());

        var ids = await db.Users.AsNoTracking()
            .Where(u => u.Department != null && EF.Functions.ILike(u.Department, Like.Exactly(requested)))
            .Select(u => u.Id)
            .ToListAsync(ct);
        var since = DateTime.UtcNow - RecentlyCompleted;
        var tasks = await WithDetails(db.Tasks.AsNoTracking())
            .Where(t => (t.AssigneeId != null && ids.Contains(t.AssigneeId.Value))
                        || (t.AssigneeId == null && t.GroupId == null && ids.Contains(t.UserId)))
            .Where(t => !t.IsCompleted || t.CompletedAt >= since)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return Ok(tasks.Select(task => TaskResponse.From(task, TaskAccess.Viewer)));
    }

    /// <summary>
    /// Moves a task someone in the manager's department is responsible for (an administrator: anyone) to another day,
    /// another level within its day, or both. A taken level pushes the person's other tasks on that day down. The
    /// reason is required: it is kept in the task's history, with what changed, and the person responsible is told.
    /// </summary>
    [HttpPut("tasks/{taskId:guid}/schedule")]
    public async Task<ActionResult<TaskResponse>> Reschedule(Guid taskId, RescheduleTaskRequest request, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        var task = await WithDetails(db.Tasks).FirstOrDefaultAsync(t => t.Id == taskId, ct);
        // A task waiting for a group is nobody's yet: it has no one to manage.
        var responsibleId = task?.AssigneeId ?? (task is { GroupId: null } ? task.UserId : null);
        if (task is null || responsibleId is null) return NotFound();
        var responsible = await db.Users.AsNoTracking().FirstAsync(u => u.Id == responsibleId, ct);
        if (role != UserRole.Admin && (responsible.Department is null || !SameDepartment(responsible.Department, user.Department)))
            return OtherDepartment();
        if (task.IsCompleted)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is completed; it can no longer be moved.");
        if (request.DueDate is { } due && task.StartDate is { } start && start > due)
        {
            ModelState.AddModelError(nameof(request.DueDate), $"The due date cannot be before the task's start date ({start:yyyy-MM-dd}).");
            return ValidationProblem(ModelState);
        }
        if (request.DueDate == task.DueDate && request.PriorityLevel == task.PriorityLevel)
            return Ok(TaskResponse.From(task, TaskAccess.Viewer));

        var (oldDue, oldLevel) = (task.DueDate, task.PriorityLevel);
        task.DueDate = request.DueDate;
        task.PriorityLevel = request.PriorityLevel;
        var pushed = new List<TaskItem>();
        if (await levels.MakeRoomAsync(task, ct, pushed) is { } full)
        {
            (task.DueDate, task.PriorityLevel) = (oldDue, oldLevel);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"Priority levels {request.PriorityLevel} to {PriorityLevels.Max} are all taken on {full:yyyy-MM-dd}: choose another level.");
        }

        var lines = new List<string>();
        if (oldDue != task.DueDate) lines.Add($"Termen: {DateText(oldDue)} → {DateText(task.DueDate)}");
        if (oldLevel != task.PriorityLevel) lines.Add($"Nivel: {LevelText(oldLevel)} → {LevelText(task.PriorityLevel)}");
        if (pushed.Count > 0)
            lines.Add("Coborâte cu un nivel: " + string.Join(", ", pushed.Select(t => $"„{t.Title}”")));
        lines.Add($"Motiv: {request.Reason.Trim()}");
        var details = string.Join("\n", lines);

        db.TaskMessages.Add(new TaskMessage { TaskId = task.Id, AuthorId = user.Id, Kind = TaskMessageKind.Rescheduled, Text = details });
        await db.SaveChangesAsync(ct);
        await notifier.TellAsync(task, responsible.Id, user.Id, TaskNotificationReason.Rescheduled, details, ct);
        return Ok(TaskResponse.From(task, TaskAccess.Viewer));
    }

    private static string LevelText(int? level) => level is { } l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : "fără nivel";

    private static readonly System.Globalization.CultureInfo Romanian = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    private static string DateText(DateOnly? date) => date is { } d ? d.ToString("d MMMM yyyy", Romanian) : "fără termen";

    private static IQueryable<TaskItem> WithDetails(IQueryable<TaskItem> tasks) =>
        tasks
            .Include(t => t.Owner)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Forwards).ThenInclude(f => f.From)
            .Include(t => t.Forwards).ThenInclude(f => f.ToUser)
            .Include(t => t.Forwards).ThenInclude(f => f.ToGroup)
            .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
            .AsSplitQuery();

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

        List<string> departments;
        if (role == UserRole.Admin) departments = await AllDepartmentsAsync(ct);
        else departments = user.Department is null ? [] : [user.Department];
        if (requested is null) return Ok(new TeamResponse(null, departments, [], TeamStats.Empty));

        var people = await db.Users.AsNoTracking()
            .Where(u => u.Department != null && EF.Functions.ILike(u.Department, Like.Exactly(requested)))
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => (t.AssigneeId != null && ids.Contains(t.AssigneeId.Value)) || (t.AssigneeId == null && t.GroupId == null && ids.Contains(t.UserId)))
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

    /// <summary>
    /// The department's open, dated tasks that fall inside a period, per person: what a calendar of
    /// the team's load is drawn from.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<TeamCalendarResponse>> GetCalendar(
        [FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to,
        [FromQuery, StringLength(Models.User.MaxDepartmentLength), SafeText] string? department,
        CancellationToken ct)
    {
        if (from > to || to!.Value.DayNumber - from!.Value.DayNumber > MaxCalendarDays)
        {
            ModelState.AddModelError(nameof(to), $"The period must run forwards and cover at most {MaxCalendarDays} days.");
            return ValidationProblem(ModelState);
        }

        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        var named = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        if (named is not null && role != UserRole.Admin && !SameDepartment(named, user.Department)) return OtherDepartment();
        var requested = named ?? user.Department;
        if (requested is null) return Ok(new TeamCalendarResponse(null, from.Value, to.Value, []));

        var people = await db.Users.AsNoTracking()
            .Where(u => u.Department != null && EF.Functions.ILike(u.Department, Like.Exactly(requested)))
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();

        var open = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsCompleted)
            .Where(t => (t.AssigneeId != null && ids.Contains(t.AssigneeId.Value)) || (t.AssigneeId == null && t.GroupId == null && ids.Contains(t.UserId)))
            .Where(t => t.DueDate == null || (t.DueDate >= from && (t.StartDate ?? t.DueDate) <= to))
            .Select(t => new { ResponsibleId = t.AssigneeId ?? t.UserId, t.Id, t.Title, t.StartDate, t.DueDate, t.Priority })
            .ToListAsync(ct);
        var byPerson = open.ToLookup(t => t.ResponsibleId);

        var members = people
            .Select(p => new CalendarMember(
                UserSummary.From(p),
                byPerson[p.Id]
                    .Where(t => t.DueDate is not null)
                    .OrderBy(t => t.StartDate ?? t.DueDate)
                    .ThenBy(t => t.DueDate)
                    .Select(t => new CalendarTask(t.Id, t.Title, t.StartDate, t.DueDate!.Value, t.Priority))
                    .ToList(),
                byPerson[p.Id].Count(t => t.DueDate is null)))
            .OrderBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var name = people.Select(p => p.Department).FirstOrDefault() ?? requested;
        return Ok(new TeamCalendarResponse(name, from.Value, to.Value, members));
    }

    private const int MaxCalendarDays = 92;

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
            .Include(t => t.Group)
            .Include(t => t.Forwards).ThenInclude(f => f.From)
            .Include(t => t.Forwards).ThenInclude(f => f.ToUser)
            .Include(t => t.Forwards).ThenInclude(f => f.ToGroup)
            .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
            .AsSplitQuery()
            .Where(t => t.AssigneeId == id || (t.AssigneeId == null && t.GroupId == null && t.UserId == id))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return Ok(tasks.Select(task => TaskResponse.From(task, TaskAccess.Viewer)));
    }

    private sealed record TaskFacts(Guid ResponsibleId, bool IsCompleted, DateOnly? DueDate, TaskPriority Priority, DateTime? CompletedAt);

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

    /// <summary>
    /// Downloads a file of a task a team member is responsible for. Managers read their people's
    /// tasks in full, files included, without being part of those tasks.
    /// </summary>
    [HttpGet("members/{id:guid}/tasks/{taskId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadMemberAttachment(Guid id, Guid taskId, Guid attachmentId, [FromQuery] bool view,
        CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        var (user, role) = me.Value;
        if (role == UserRole.User) return NotAManager();

        var member = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (member is null) return NotFound();
        if (role != UserRole.Admin && (member.Department is null || !SameDepartment(member.Department, user.Department)))
            return OtherDepartment();

        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a =>
            a.Id == attachmentId && a.TaskId == taskId
            && db.Tasks.Any(t => t.Id == taskId
                && (t.AssigneeId == id || (t.AssigneeId == null && t.GroupId == null && t.UserId == id))), ct);
        return attachment is null ? NotFound() : await AttachmentDownload.SendAsync(this, attachments, attachment, logger, view, ct);
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
