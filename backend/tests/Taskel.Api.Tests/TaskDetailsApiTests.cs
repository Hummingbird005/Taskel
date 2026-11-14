using System.Net;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class TaskDetailsApiTests(TaskelApiFactory factory)
{
    [Fact]
    public async Task Create_Without_Details_Uses_Defaults()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostApiAsync("/api/tasks", new { title = "Plain task" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var task = (await response.Content.ReadApiAsync<TaskItem>())!;
        Assert.Null(task.DueDate);
        Assert.Equal(TaskPriority.Medium, task.Priority);
        Assert.Empty(task.Tags);
    }

    [Fact]
    public async Task Create_With_Details_Persists_Them()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var due = new DateOnly(2027, 3, 15);

        var response = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Ship release", null, due, TaskPriority.High, ["work", "release"]));
        var created = (await response.Content.ReadApiAsync<TaskItem>())!;

        var stored = (await client.GetApiAsync<TaskItem>($"/api/tasks/{created.Id}"))!;
        Assert.Equal(due, stored.DueDate);
        Assert.Equal(TaskPriority.High, stored.Priority);
        Assert.Equal(["work", "release"], stored.Tags);
    }

    private static readonly string[] OneTag = ["a"];

    [Fact]
    public async Task Wire_Format_Uses_String_Priority_And_Iso_Date()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostApiAsync("/api/tasks",
            new { title = "Wire", dueDate = "2027-01-31", priority = "low", tags = OneTag });
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"dueDate\":\"2027-01-31\"", json);
        Assert.Contains("\"priority\":\"low\"", json);
        Assert.Contains("\"tags\":[\"a\"]", json);
    }

    [Fact]
    public async Task Low_Priority_Is_Not_Replaced_By_The_Default()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Someday", null, null, TaskPriority.Low));
        var created = (await response.Content.ReadApiAsync<TaskItem>())!;

        var stored = (await client.GetApiAsync<TaskItem>($"/api/tasks/{created.Id}"))!;
        Assert.Equal(TaskPriority.Low, stored.Priority);
    }

    [Fact]
    public async Task Tags_Are_Trimmed_LowerCased_And_Deduplicated()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Tagged", null, Tags: ["  Work ", "work", "", "Home", "HOME", "   "]));
        var task = (await response.Content.ReadApiAsync<TaskItem>())!;

        Assert.Equal(["work", "home"], task.Tags);
    }

    [Fact]
    public async Task Update_Replaces_And_Clears_Details()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Evolving", null, new DateOnly(2027, 1, 1), TaskPriority.High, ["a", "b"]));
        var task = (await created.Content.ReadApiAsync<TaskItem>())!;

        var changed = await client.PutApiAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Evolving", null, false, new DateOnly(2027, 2, 2), TaskPriority.Low, ["c"]));
        var afterChange = (await changed.Content.ReadApiAsync<TaskItem>())!;
        Assert.Equal(new DateOnly(2027, 2, 2), afterChange.DueDate);
        Assert.Equal(TaskPriority.Low, afterChange.Priority);
        Assert.Equal(["c"], afterChange.Tags);

        await client.PutApiAsync($"/api/tasks/{task.Id}", new { title = "Evolving", isCompleted = true });
        var afterClear = (await client.GetApiAsync<TaskItem>($"/api/tasks/{task.Id}"))!;
        Assert.Null(afterClear.DueDate);
        Assert.Equal(TaskPriority.Medium, afterClear.Priority);
        Assert.Empty(afterClear.Tags);
        Assert.True(afterClear.IsCompleted);
    }

    public static TheoryData<string> InvalidBodies => ApiJson.Bodies(
    [
        new { title = "x", priority = "urgent" },
        new { title = "x", priority = 2 },
        new { title = "x", priority = (string?)null },
        new { title = "x", dueDate = "next tuesday" },
        new { title = "x", dueDate = "2027-02-30" },
        new { title = "x", tags = Enumerable.Range(0, TagList.MaxTags + 1).Select(i => $"tag{i}").ToArray() },
        new { title = "x", tags = new[] { new string('a', TagList.MaxTagLength + 1) } },
    ]);

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_Details_Return_BadRequest(string body)
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostApiAsync("/api/tasks", ApiJson.Element(body))).StatusCode);

        var created = await client.PostApiAsync("/api/tasks", new { title = "valid" });
        var task = (await created.Content.ReadApiAsync<TaskItem>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutApiAsync($"/api/tasks/{task.Id}", ApiJson.Element(body))).StatusCode);
    }

    [Fact]
    public async Task Duplicate_Tags_Do_Not_Count_Towards_The_Limit()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tags = Enumerable.Repeat("same", TagList.MaxTags + 5).ToList();

        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Dupes", null, Tags: tags));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["same"], (await response.Content.ReadApiAsync<TaskItem>())!.Tags);
    }
}
