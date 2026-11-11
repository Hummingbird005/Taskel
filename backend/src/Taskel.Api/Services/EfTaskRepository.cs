using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

public class EfTaskRepository(TaskelDbContext db) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken ct = default) =>
        await db.Tasks.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);

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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0;
}
