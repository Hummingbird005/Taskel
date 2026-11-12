using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

public class EfTaskRepository(TaskelDbContext db) : ITaskRepository
{
    private IQueryable<TaskItem> VisibleTo(Guid userId) =>
        db.Tasks
            .Include(t => t.Owner)
            .Include(t => t.Assignee)
            .Where(t => t.UserId == userId || t.AssigneeId == userId);

    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(Guid userId, CancellationToken ct = default) =>
        await VisibleTo(userId).AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public Task<TaskItem?> GetAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        VisibleTo(userId).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TaskItem> AddAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Add(item);
        await db.SaveChangesAsync(ct);
        await LoadPeopleAsync(item, ct);
        return item;
    }

    public async Task SaveAsync(TaskItem item, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await LoadPeopleAsync(item, ct);
    }

    public async Task DeleteAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Id == userId, ct);

    /// <summary>Loads the owner and assignee, which change when a task is created or reassigned.</summary>
    private async Task LoadPeopleAsync(TaskItem item, CancellationToken ct)
    {
        await db.Entry(item).Reference(t => t.Owner).LoadAsync(ct);
        if (item.AssigneeId is null) item.Assignee = null;
        else await db.Entry(item).Reference(t => t.Assignee).LoadAsync(ct);
    }
}
