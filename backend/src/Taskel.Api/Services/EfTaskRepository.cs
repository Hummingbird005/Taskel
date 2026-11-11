using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

public class EfTaskRepository(TaskelDbContext db) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(Guid userId, CancellationToken ct = default) =>
        await db.Tasks.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public Task<TaskItem?> GetAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

    public async Task<TaskItem> AddAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task UpdateAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Update(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await db.Tasks.Where(t => t.Id == id && t.UserId == userId).ExecuteDeleteAsync(ct) > 0;
}
