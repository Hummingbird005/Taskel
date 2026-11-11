using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class TasksApiTests(TaskelApiFactory factory)
{
    [Fact]
    public async Task Health_ReturnsOk_WithoutAuthentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tasks_RequireAuthentication()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostApiAsync("/api/tasks", new CreateTaskRequest("x", null))).StatusCode);
    }

    [Fact]
    public async Task Task_Crud_Flow_Works()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Write docs", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Content.ReadApiAsync<TaskItem>();
        Assert.NotNull(task);

        var updated = await client.PutApiAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Write docs", "README", true));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True((await updated.Content.ReadApiAsync<TaskItem>())!.IsCompleted);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyTitle_ReturnsBadRequest()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostApiAsync("/api/tasks", new { title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Created_Task_Is_Persisted_And_Listed()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Persist me", "in postgres"));
        var task = await created.Content.ReadApiAsync<TaskItem>();

        var all = await client.GetApiAsync<List<TaskItem>>("/api/tasks");
        Assert.Contains(all!, t => t.Id == task!.Id && t.Description == "in postgres");
    }

    [Fact]
    public async Task Update_And_Delete_Unknown_Task_Return_NotFound()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutApiAsync($"/api/tasks/{id}", new UpdateTaskRequest("x", null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/tasks/{id}")).StatusCode);
    }

    [Fact]
    public async Task Users_Cannot_See_Or_Change_Each_Others_Tasks()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();

        var created = await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("Alice's secret", null));
        var task = (await created.Content.ReadApiAsync<TaskItem>())!;

        Assert.Empty((await bob.GetApiAsync<List<TaskItem>>("/api/tasks"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("hacked", null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);

        var stillThere = await alice.GetApiAsync<TaskItem>($"/api/tasks/{task.Id}");
        Assert.Equal("Alice's secret", stillThere!.Title);
    }
}
