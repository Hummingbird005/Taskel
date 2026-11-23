using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

public class EfTaskRepository(TaskelDbContext db) : ITaskRepository
{
    private IQueryable<TaskItem> WithDetails() =>
        db.Tasks
            .Include(t => t.Owner)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Forwards).ThenInclude(f => f.From)
            .Include(t => t.Forwards).ThenInclude(f => f.ToUser)
            .Include(t => t.Forwards).ThenInclude(f => f.ToGroup)
            .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
            .Include(t => t.ParentTask)
            .Include(t => t.Distributions).ThenInclude(d => d.Assignee)
            .Include(t => t.Distributions).ThenInclude(d => d.Group)
            .AsSplitQuery();

    private IQueryable<TaskItem> VisibleTo(Guid userId) =>
        WithDetails().Where(t =>
            t.UserId == userId
            || t.AssigneeId == userId
            || (t.AssigneeId == null && t.GroupId != null
                && db.GroupMembers.Any(m => m.GroupId == t.GroupId && m.UserId == userId))
            || t.Forwards.Any(f => f.FromUserId == userId));

    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(Guid userId, CancellationToken ct = default) =>
        await VisibleTo(userId).AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public Task<TaskItem?> GetAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        VisibleTo(userId).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TaskItem> AddAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Add(item);
        await db.SaveChangesAsync(ct);
        return await ReloadAsync(item.Id, ct);
    }

    public async Task SaveAsync(TaskItem item, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await LoadDetailsAsync(item, ct);
    }

    public async Task DeleteAsync(TaskItem item, CancellationToken ct = default)
    {
        db.Tasks.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Id == userId, ct);

    public Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct = default) =>
        db.Groups.AnyAsync(g => g.Id == groupId, ct);

    public async Task<IReadOnlySet<Guid>> GetGroupIdsAsync(Guid userId, CancellationToken ct = default)
    {
        // A block body on purpose: some compilers and IDEs reject the expression-bodied form here.
        return (await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync(ct)).ToHashSet();
    }

    private Task<TaskItem> ReloadAsync(Guid id, CancellationToken ct) => WithDetails().FirstAsync(t => t.Id == id, ct);

    /// <summary>Loads the people, group and forwards of a tracked task after they changed.</summary>
    private async Task LoadDetailsAsync(TaskItem item, CancellationToken ct)
    {
        var entry = db.Entry(item);
        await entry.Reference(t => t.Owner).LoadAsync(ct);
        if (item.AssigneeId is null) item.Assignee = null;
        else await entry.Reference(t => t.Assignee).LoadAsync(ct);
        if (item.GroupId is null) item.Group = null;
        else await entry.Reference(t => t.Group).LoadAsync(ct);
        await entry.Collection(t => t.Forwards).Query()
            .Include(f => f.From).Include(f => f.ToUser).Include(f => f.ToGroup)
            .LoadAsync(ct);
        await entry.Collection(t => t.Attachments).Query().Include(a => a.UploadedBy).LoadAsync(ct);
        if (item.ParentTaskId is not null) await entry.Reference(t => t.ParentTask).LoadAsync(ct);
        await entry.Collection(t => t.Distributions).Query().Include(d => d.Assignee).Include(d => d.Group).LoadAsync(ct);
    }

    public Task<bool> IsFileSharedAsync(string storedName, CancellationToken ct = default) =>
        db.Attachments.AnyAsync(a => a.StoredName == storedName, ct);
}
