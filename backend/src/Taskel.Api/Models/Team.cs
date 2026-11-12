namespace Taskel.Api.Models;

/// <summary>Workload figures for one person, or summed for a department.</summary>
/// <param name="Open">Tasks not yet completed.</param>
/// <param name="Overdue">Open tasks whose due date has passed.</param>
/// <param name="DueToday">Open tasks due today.</param>
/// <param name="DueNext7Days">Open tasks due in the 7 days after today.</param>
/// <param name="HighPriorityOpen">Open tasks with high priority.</param>
/// <param name="CompletedLast7Days">Tasks completed in the last 7 days.</param>
/// <param name="CompletedLast30Days">Tasks completed in the last 30 days.</param>
public record TeamStats(
    int Open,
    int Overdue,
    int DueToday,
    int DueNext7Days,
    int HighPriorityOpen,
    int CompletedLast7Days,
    int CompletedLast30Days)
{
    public static readonly TeamStats Empty = new(0, 0, 0, 0, 0, 0, 0);

    public static TeamStats Sum(IEnumerable<TeamStats> all) => all.Aggregate(Empty, (a, b) => new TeamStats(
        a.Open + b.Open, a.Overdue + b.Overdue, a.DueToday + b.DueToday, a.DueNext7Days + b.DueNext7Days,
        a.HighPriorityOpen + b.HighPriorityOpen, a.CompletedLast7Days + b.CompletedLast7Days,
        a.CompletedLast30Days + b.CompletedLast30Days));
}

public record TeamMember(UserSummary User, UserRole Role, TeamStats Stats);

/// <summary>A department's people and their workload, as a manager sees it.</summary>
/// <param name="Department">The department shown; null when the manager has none set.</param>
/// <param name="Departments">The departments the caller may look at: their own, or all of them for an administrator.</param>
public record TeamResponse(string? Department, IReadOnlyList<string> Departments, IReadOnlyList<TeamMember> Members, TeamStats Totals);
