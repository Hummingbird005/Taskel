using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>
/// Task storage. A user can reach the tasks they created and the tasks assigned to them;
/// every read is scoped to that. Returned tasks have their owner and assignee loaded.
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
}
