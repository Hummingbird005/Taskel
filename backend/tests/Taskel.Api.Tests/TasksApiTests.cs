using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

public class TasksApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Task_Crud_Flow_Works()
    {
        var created = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Write docs", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Content.ReadFromJsonAsync<TaskItem>();
        Assert.NotNull(task);

        var updated = await _client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Write docs", "README", true));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True((await updated.Content.ReadFromJsonAsync<TaskItem>())!.IsCompleted);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyTitle_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
