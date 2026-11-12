using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>
/// Task storage. A user can reach the tasks they created, the tasks assigned to them, the unclaimed
/// tasks of the groups they belong to, and the tasks they passed on; every read is scoped to that.
/// Returned tasks have their people, group and forwards loaded.
/// </summary>
public interface ITaskRepository
{
    Task<IReadOnlyList<TaskItem>> GetAllAsync(Guid userId, CancellationToken ct = default);
    /// <summary>The task, tracked for changes, or null when the user cannot reach it.</summary>
    Task<TaskItem?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<TaskItem> AddAsync(TaskItem item, CancellationToken ct = default);
    /// <summary>Saves changes made to a task returned by <see cref="GetAsync"/>.</summary>
    Task SaveAsync(TaskItem item, CancellationToken ct = default);
    Task DeleteAsync(TaskItem item, CancellationToken ct = default);
    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>The groups a user belongs to.</summary>
    Task<IReadOnlySet<Guid>> GetGroupIdsAsync(Guid userId, CancellationToken ct = default);
}
