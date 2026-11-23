using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>
/// The priority level of a task: its order within a day, 1 first. Within one day, one person has at most one open
/// task at each level. Giving a task a level that is taken pushes the task holding it down by one, and the one below
/// that too if it was taken, until a free level is reached; nothing may go past <see cref="Max"/>.
/// </summary>
/// <remarks>
/// The person is the one the task is with: its assignee, or its creator while it is with nobody else. A task waiting
/// for a group has no person yet, and no rule. Completed tasks keep their level but no longer hold a place.
/// </remarks>
public class PriorityLevels(TaskelDbContext db)
{
    public const int Max = 20;

    /// <summary>
    /// Makes room for <paramref name="task"/>'s level on its day, pushing the others down. Changes the tracked tasks;
    /// the caller saves. Returns null when it fits, or the day on which levels would go past <see cref="Max"/>, in
    /// which case nothing was changed.
    /// </summary>
    /// <param name="moved">When given, receives the tasks that were pushed down.</param>
    public async Task<DateOnly?> MakeRoomAsync(TaskItem task, CancellationToken ct, List<TaskItem>? moved = null)
    {
        if (task.PriorityLevel is not { } level || task.DueDate is not { } day || task.IsCompleted) return null;
        var person = task.AssigneeId ?? (task.GroupId is null ? task.UserId : null);
        if (person is null) return null;

        var others = await db.Tasks
            .Where(t => t.Id != task.Id && !t.IsCompleted && t.DueDate == day && t.PriorityLevel != null
                        && (t.AssigneeId == person || (t.AssigneeId == null && t.GroupId == null && t.UserId == person)))
            .OrderBy(t => t.PriorityLevel)
            .ToListAsync(ct);

        // The run of consecutive levels starting at the task's own moves down by one; the first gap stops it.
        var moving = new List<TaskItem>();
        var next = level;
        foreach (var other in others.Where(o => o.PriorityLevel >= level))
        {
            if (other.PriorityLevel != next) break;
            moving.Add(other);
            next++;
        }
        if (moving.Count > 0 && moving[^1].PriorityLevel >= Max) return day;
        foreach (var other in moving) other.PriorityLevel++;
        moved?.AddRange(moving);
        return null;
    }

    /// <summary>
    /// For changes the person did not ask for themselves (a reassignment, a forward, a moved due date): makes room, or,
    /// when the day is full, takes the task's level away rather than refusing the change.
    /// </summary>
    public async Task FitOrDropAsync(TaskItem task, CancellationToken ct)
    {
        if (await MakeRoomAsync(task, ct) is not null) task.PriorityLevel = null;
    }
}
