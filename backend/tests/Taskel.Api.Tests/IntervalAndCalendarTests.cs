using System.Net;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class IntervalAndCalendarTests(TaskelApiFactory factory)
{
    private static readonly DateOnly Day = new(2027, 3, 10); // a Wednesday, well away from "today"

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, DateOnly? start, DateOnly? due,
        Guid? assigneeId = null)
    {
        var response = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest(title, null, due, AssigneeId: assigneeId, StartDate: start));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<int> WorkloadAsync(HttpClient client, Guid userId, DateOnly from, DateOnly to, Guid? exclude = null)
    {
        var url = $"/api/users/{userId}/workload?from={D(from)}&to={D(to)}" + (exclude is null ? "" : $"&excludeTaskId={exclude}");
        return (await client.GetApiAsync<UserWorkloadResponse>(url))!.Count;
    }

    // ---------- interval on a task ----------

    [Fact]
    public async Task Task_Can_Have_An_Interval()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var task = await CreateAsync(client, "Audit", Day, Day.AddDays(4));

        Assert.Equal(Day, task.StartDate);
        Assert.Equal(Day.AddDays(4), task.DueDate);
        var json = await client.GetStringAsync($"/api/tasks/{task.Id}");
        Assert.Contains("\"startDate\":\"2027-03-10\"", json);

        // A one-day interval is fine, and a task without a start date still works as before.
        Assert.Equal(Day, (await CreateAsync(client, "One day", Day, Day)).StartDate);
        Assert.Null((await CreateAsync(client, "Deadline only", null, Day)).StartDate);
    }

    public static TheoryData<string, string> InvalidIntervals => new()
    {
        { "start without due", """{"title":"x","startDate":"2027-03-10"}""" },
        { "start after due", """{"title":"x","startDate":"2027-03-11","dueDate":"2027-03-10"}""" },
        { "start out of range", """{"title":"x","startDate":"1999-01-01","dueDate":"2027-03-10"}""" },
        { "start not a date", """{"title":"x","startDate":"tomorrow","dueDate":"2027-03-10"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidIntervals))]
    public async Task Invalid_Interval_Is_Rejected_On_Create_And_Update(string reason, string body)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var content = () => new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var created = await client.PostAsync("/api/tasks", content());
        Assert.True(created.StatusCode == HttpStatusCode.BadRequest, $"create: {reason} gave {(int)created.StatusCode}");

        var task = await CreateAsync(client, "Valid", Day, Day.AddDays(1));
        var updated = await client.PutAsync($"/api/tasks/{task.Id}", content());
        Assert.True(updated.StatusCode == HttpStatusCode.BadRequest, $"update: {reason} gave {(int)updated.StatusCode}");
        Assert.Equal(Day, (await client.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.StartDate);
    }

    [Fact]
    public async Task Update_Changes_And_Clears_The_Interval()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var task = await CreateAsync(client, "Moves", Day, Day.AddDays(2));

        var moved = await client.PutApiAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Moves", null, false, Day.AddDays(9), StartDate: Day.AddDays(7)));
        Assert.Equal(Day.AddDays(7), (await moved.Content.ReadApiAsync<TaskResponse>())!.StartDate);

        // Update is a full replace: leaving the start date out removes it.
        await client.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Moves", null, false, Day.AddDays(9)));
        var cleared = (await client.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.Null(cleared.StartDate);
        Assert.Equal(Day.AddDays(9), cleared.DueDate);
    }

    // ---------- workload check before assigning ----------

    [Fact]
    public async Task Workload_Counts_Open_Tasks_That_Overlap_The_Period()
    {
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (boss, _) = await factory.CreateUserAsync();

        await CreateAsync(ana, "Interval before, touching the first day", Day.AddDays(-3), Day);
        await CreateAsync(ana, "Single day inside", null, Day.AddDays(1));
        await CreateAsync(ana, "Interval covering everything", Day.AddDays(-10), Day.AddDays(10));
        await CreateAsync(boss, "Assigned to Ana, starting on the last day", Day.AddDays(2), Day.AddDays(6), anaUser.Id);
        // None of these count:
        await CreateAsync(ana, "Ends the day before", Day.AddDays(-5), Day.AddDays(-1));
        await CreateAsync(ana, "Starts the day after", Day.AddDays(3), Day.AddDays(4));
        await CreateAsync(ana, "No date at all", null, null);
        await CreateAsync(ana, "Given away to the boss", null, Day, (await boss.GetApiAsync<UserResponse>("/api/auth/me"))!.Id);
        var finished = await CreateAsync(ana, "Already done", null, Day);
        await ana.PostApiAsync($"/api/tasks/{finished.Id}/complete", new { });

        // Anyone signed in may ask, and gets only the number.
        Assert.Equal(4, await WorkloadAsync(boss, anaUser.Id, Day, Day.AddDays(2)));
        Assert.Equal("{\"count\":4}", await boss.GetStringAsync($"/api/users/{anaUser.Id}/workload?from={D(Day)}&to={D(Day.AddDays(2))}"));

        // A single day.
        Assert.Equal(2, await WorkloadAsync(boss, anaUser.Id, Day, Day));
        Assert.Equal(1, await WorkloadAsync(boss, anaUser.Id, Day.AddDays(20), Day.AddDays(20)) + 1);
    }

    [Fact]
    public async Task Workload_Can_Leave_Out_The_Task_Being_Edited()
    {
        var (ana, anaUser) = await factory.CreateUserAsync();
        var edited = await CreateAsync(ana, "Being edited", Day, Day.AddDays(1));
        await CreateAsync(ana, "Another", null, Day);

        Assert.Equal(2, await WorkloadAsync(ana, anaUser.Id, Day, Day.AddDays(1)));
        Assert.Equal(1, await WorkloadAsync(ana, anaUser.Id, Day, Day.AddDays(1), exclude: edited.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2027-03-10")]
    [InlineData("?to=2027-03-10")]
    [InlineData("?from=2027-03-11&to=2027-03-10")]
    [InlineData("?from=2026-01-01&to=2027-06-01")]
    [InlineData("?from=soon&to=2027-03-10")]
    [InlineData("?from=2027-03-10&to=2027-03-10&excludeTaskId=nope")]
    public async Task Workload_Rejects_A_Bad_Period(string query)
    {
        var (client, user) = await factory.CreateUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/users/{user.Id}/workload{query}")).StatusCode);
    }

    [Fact]
    public async Task Workload_Needs_A_Known_User_And_A_Session()
    {
        var (client, user) = await factory.CreateUserAsync();
        var query = $"?from={D(Day)}&to={D(Day)}";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{Guid.NewGuid()}/workload{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/users/{user.Id}/workload{query}")).StatusCode);
    }

    // ---------- team calendar ----------

    private async Task<(HttpClient Client, UserResponse User)> CreateAsync(UserRole role, string? department)
    {
        var created = await factory.CreateUserAsync();
        await factory.SetRoleAsync(created.User.Id, role, department);
        return created;
    }

    [Fact]
    public async Task Manager_Gets_The_Departments_Dated_Open_Tasks_For_A_Period()
    {
        var department = $"Dept {Guid.NewGuid():N}"[..20];
        var (manager, managerUser) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        var (outsider, _) = await CreateAsync(UserRole.User, "Elsewhere " + Guid.NewGuid().ToString("N")[..6]);

        var interval = await CreateAsync(ana, "Interval", Day, Day.AddDays(3));
        var single = await CreateAsync(ana, "Single day", null, Day.AddDays(1));
        await CreateAsync(manager, "Assigned to Ana", Day.AddDays(6), Day.AddDays(8), anaUser.Id);
        await CreateAsync(ana, "Before the period", null, Day.AddDays(-1));
        await CreateAsync(ana, "After the period", Day.AddDays(20), Day.AddDays(21));
        await CreateAsync(ana, "Undated 1", null, null);
        await CreateAsync(ana, "Undated 2", null, null);
        var done = await CreateAsync(ana, "Done", null, Day);
        await ana.PostApiAsync($"/api/tasks/{done.Id}/complete", new { });
        await CreateAsync(outsider, "Other department", null, Day);

        var calendar = (await manager.GetApiAsync<TeamCalendarResponse>($"/api/team/calendar?from={D(Day)}&to={D(Day.AddDays(13))}"))!;

        Assert.Equal(department, calendar.Department);
        Assert.Equal(2, calendar.Members.Count);
        var anaRow = calendar.Members.Single(m => m.User.Id == anaUser.Id);
        Assert.Equal(["Interval", "Single day", "Assigned to Ana"], anaRow.Tasks.Select(t => t.Title));
        Assert.Equal(new CalendarTask(interval.Id, "Interval", Day, Day.AddDays(3), TaskPriority.Medium), anaRow.Tasks[0]);
        Assert.Null(anaRow.Tasks.Single(t => t.Id == single.Id).StartDate);
        Assert.Equal(2, anaRow.Unscheduled);
        // The task the manager handed to Ana is Ana's, not the manager's.
        Assert.Empty(calendar.Members.Single(m => m.User.Id == managerUser.Id).Tasks);
    }

    [Fact]
    public async Task Calendar_Is_For_Managers_Of_That_Department_Only()
    {
        var department = $"Dept {Guid.NewGuid():N}"[..20];
        var query = $"from={D(Day)}&to={D(Day.AddDays(6))}";
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (member, _) = await CreateAsync(UserRole.User, department);
        var (admin, _) = await CreateAsync(UserRole.Admin, null);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/team/calendar?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/team/calendar?{query}&department=Other")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/team/calendar?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/team/calendar?{query}&department={Uri.EscapeDataString(department)}")).StatusCode);

        var noDepartment = await CreateAsync(UserRole.Manager, null);
        var empty = (await noDepartment.Client.GetApiAsync<TeamCalendarResponse>($"/api/team/calendar?{query}"))!;
        Assert.Null(empty.Department);
        Assert.Empty(empty.Members);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2027-03-10")]
    [InlineData("?from=2027-03-11&to=2027-03-10")]
    [InlineData("?from=2027-01-01&to=2027-06-01")]
    public async Task Calendar_Rejects_A_Bad_Period(string query)
    {
        var (manager, _) = await CreateAsync(UserRole.Manager, "Any");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"/api/team/calendar{query}")).StatusCode);
    }
}
