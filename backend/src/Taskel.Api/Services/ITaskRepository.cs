using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>All operations are scoped to the owning user.</summary>
public interface ITaskRepository
{
    Task<IReadOnlyList<TaskItem>> GetAllAsync(Guid userId, CancellationToken ct = default);
    Task<TaskItem?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<TaskItem> AddAsync(TaskItem item, CancellationToken ct = default);
    Task UpdateAsync(TaskItem item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
