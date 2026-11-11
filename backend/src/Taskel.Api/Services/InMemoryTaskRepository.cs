using System.Collections.Concurrent;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>Simple thread-safe in-memory store. Replace with a database-backed implementation later.</summary>
public class InMemoryTaskRepository : ITaskRepository
{
    private readonly ConcurrentDictionary<Guid, TaskItem> _items = new();

    public IReadOnlyList<TaskItem> GetAll() =>
        _items.Values.OrderBy(t => t.CreatedAt).ToList();

    public TaskItem? Get(Guid id) => _items.GetValueOrDefault(id);

    public TaskItem Add(TaskItem item)
    {
        _items[item.Id] = item;
        return item;
    }

    public bool Update(TaskItem item)
    {
        if (!_items.ContainsKey(item.Id)) return false;
        _items[item.Id] = item;
        return true;
    }

    public bool Delete(Guid id) => _items.TryRemove(id, out _);
}
