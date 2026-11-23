using System.Net;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// The priority level, 1 to 20: within one day, one person has one open task per level; a level already taken
/// pushes the others down, never past 20.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriorityLevelTests(TaskelApiFactory factory)
{
    private static readonly DateOnly Day = new(2026, 12, 9);

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string title, int? level, DateOnly? due = null,
        Guid? assigneeId = null) =>
        await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, due ?? Day, AssigneeId: assigneeId, PriorityLevel: level));

    private static async Task<TaskResponse> CreatedAsync(HttpClient client, string title, int? level, DateOnly? due = null, Guid? assigneeId = null)
    {
        var response = await CreateAsync(client, title, level, due, assigneeId);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<Dictionary<string, int?>> LevelsAsync(HttpClient client) =>
        (await client.GetApiAsync<List<TaskResponse>>("/api/tasks"))!.ToDictionary(t => t.Title, t => t.PriorityLevel);

    [Fact]
    public async Task A_Taken_Level_Pushes_The_Run_Below_It_Down_And_Stops_At_The_First_Gap()
    {
        var (ana, _) = await factory.CreateUserAsync();
        await CreatedAsync(ana, "A", 1);
        await CreatedAsync(ana, "B", 2);
        await CreatedAsync(ana, "D", 4);
        await CreatedAsync(ana, "Alta zi", 1, Day.AddDays(1));

        var urgent = await CreatedAsync(ana, "Urgent", 1);

        Assert.Equal(1, urgent.PriorityLevel);
        var levels = await LevelsAsync(ana);
        Assert.Equal(2, levels["A"]);
        Assert.Equal(3, levels["B"]);
        // 3 was free: D stays where it was. Another day is not touched.
        Assert.Equal(4, levels["D"]);
        Assert.Equal(1, levels["Alta zi"]);
    }

    [Fact]
    public async Task No_Level_Goes_Past_Twenty()
    {
        var (ana, _) = await factory.CreateUserAsync();
        for (var level = 1; level <= 20; level++) await CreatedAsync(ana, $"T{level}", level);

        var refused = await CreateAsync(ana, "Prea mult", 1);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var levels = await LevelsAsync(ana);
        Assert.Equal(1, levels["T1"]);
        Assert.Equal(20, levels["T20"]);
        Assert.False(levels.ContainsKey("Prea mult"));
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(ana, "Zero", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(ana, "Douăzeci și unu", 21)).StatusCode);
    }

    [Fact]
    public async Task The_Rule_Is_Per_Person_And_Ignores_Completed_Tasks_And_Tasks_Without_A_Level()
    {
        var (ana, _) = await factory.CreateUserAsync();
        var (manager, _) = await factory.CreateUserAsync();
        var (_, dan) = await factory.CreateUserAsync();
        await CreatedAsync(ana, "Al Anei", 1);
        // The manager's own task at 1, and one he gave to Dan at 1: each person has their own day.
        await CreatedAsync(manager, "Al managerului", 1);
        await CreatedAsync(manager, "Al lui Dan", 1, assigneeId: dan.Id);
        var done = await CreatedAsync(ana, "Terminat", 2);
        await ana.PutApiAsync($"/api/tasks/{done.Id}", new UpdateTaskRequest(done.Title, null, true, Day, PriorityLevel: 2));
        await CreatedAsync(ana, "Fără nivel", null);

        await CreatedAsync(ana, "Nou", 2);

        var levels = await LevelsAsync(ana);
        Assert.Equal(1, levels["Al Anei"]);
        Assert.Equal(2, levels["Nou"]);
        Assert.Equal(2, levels["Terminat"]);
        Assert.Null(levels["Fără nivel"]);
        var managers = await LevelsAsync(manager);
        Assert.Equal(1, managers["Al managerului"]);
        Assert.Equal(1, managers["Al lui Dan"]);
    }

    [Fact]
    public async Task Changing_The_Level_Or_The_Day_Makes_Room_On_The_New_One()
    {
        var (ana, _) = await factory.CreateUserAsync();
        await CreatedAsync(ana, "Primul", 1);
        var moved = await CreatedAsync(ana, "Mutat", 1, Day.AddDays(1));

        var response = await ana.PutApiAsync($"/api/tasks/{moved.Id}",
            new UpdateTaskRequest(moved.Title, null, false, Day, PriorityLevel: 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var levels = await LevelsAsync(ana);
        Assert.Equal(1, levels["Mutat"]);
        Assert.Equal(2, levels["Primul"]);

        // Leaving the level out removes it.
        await ana.PutApiAsync($"/api/tasks/{moved.Id}", new UpdateTaskRequest(moved.Title, null, false, Day));
        Assert.Null((await LevelsAsync(ana))["Mutat"]);
    }

    [Fact]
    public async Task A_Task_Given_To_A_Full_Day_Loses_Its_Level_Instead_Of_Being_Refused()
    {
        var (dan, danUser) = await factory.CreateUserAsync();
        var (manager, _) = await factory.CreateUserAsync();
        for (var level = 1; level <= 20; level++) await CreatedAsync(dan, $"D{level}", level);
        var task = await CreatedAsync(manager, "Pentru Dan", 1);

        var assigned = await manager.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(danUser.Id));

        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Null((await assigned.Content.ReadApiAsync<TaskResponse>())!.PriorityLevel);
        Assert.Equal(1, (await LevelsAsync(dan))["D1"]);
    }

    [Fact]
    public async Task A_Task_Given_To_Someone_Takes_Its_Level_On_Their_Day()
    {
        var (dan, danUser) = await factory.CreateUserAsync();
        var (manager, _) = await factory.CreateUserAsync();
        await CreatedAsync(dan, "Al lui Dan", 1);

        await CreatedAsync(manager, "Urgent pentru Dan", 1, assigneeId: danUser.Id);

        var levels = await LevelsAsync(dan);
        Assert.Equal(1, levels["Urgent pentru Dan"]);
        Assert.Equal(2, levels["Al lui Dan"]);
    }
}
