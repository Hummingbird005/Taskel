using Taskel.Api.Models;

namespace Taskel.Api.Services;

public interface ITaskRepository
{
    IReadOnlyList<TaskItem> GetAll();
    TaskItem? Get(Guid id);
    TaskItem Add(TaskItem item);
    bool Update(TaskItem item);
    bool Delete(Guid id);
}
