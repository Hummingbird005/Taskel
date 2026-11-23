using System.Net;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>A manager's list of every task in their department, and moving the due dates of those tasks.</summary>
[Collection(ApiCollection.Name)]
public class TeamTasksTests(TaskelApiFactory factory)
{
    private static string NewDepartment() => $"Dept {Guid.NewGuid():N}"[..20];

    private async Task<(HttpClient Client, UserResponse User)> CreateAsync(UserRole role, string? department)
    {
        var created = await factory.CreateUserAsync();
        await factory.SetRoleAsync(created.User.Id, role, department);
        return created;
    }

    private static async Task<TaskResponse> TaskAsync(HttpClient client, string title, DateOnly? due = null,
        DateOnly? start = null, Guid? assigneeId = null, Guid? groupId = null)
    {
        var response = await client.PostApiAsync("/api/tasks",
            new CreateTaskRequest(title, null, due, AssigneeId: assigneeId, GroupId: groupId, StartDate: start));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid taskId, string? dueDate,
        int? priorityLevel = null, string? reason = "Prioritate schimbată") =>
        client.PutApiAsync($"/api/team/tasks/{taskId}/schedule", new { dueDate, priorityLevel, reason });

    private static async Task<int?> LevelAsync(HttpClient client, Guid taskId) =>
        (await client.GetApiAsync<TaskResponse>($"/api/tasks/{taskId}"))!.PriorityLevel;

    [Fact]
    public async Task A_Manager_Sees_Every_Task_Their_People_Are_Responsible_For()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        var (outsider, outsiderUser) = await CreateAsync(UserRole.User, NewDepartment());

        var own = await TaskAsync(ana, "Al Anei", new DateOnly(2026, 12, 3));
        var given = await TaskAsync(outsider, "Dat Anei", assigneeId: anaUser.Id);
        var done = await TaskAsync(ana, "Terminat");
        await ana.PutApiAsync($"/api/tasks/{done.Id}", new UpdateTaskRequest(done.Title, null, true));
        // Not Ana's: given away by her, and someone else's in another department.
        var away = await TaskAsync(ana, "Dat altcuiva", assigneeId: outsiderUser.Id);
        var foreign = await TaskAsync(outsider, "Din alt departament");

        var tasks = (await manager.GetApiAsync<List<TaskResponse>>("/api/team/tasks"))!;

        var ids = tasks.Select(t => t.Id).ToList();
        Assert.Contains(own.Id, ids);
        Assert.Contains(given.Id, ids);
        Assert.Contains(done.Id, ids);
        Assert.DoesNotContain(away.Id, ids);
        Assert.DoesNotContain(foreign.Id, ids);
        Assert.All(tasks, t => Assert.Equal(TaskAccess.Viewer, t.Access));
    }

    [Fact]
    public async Task Only_Managers_See_It_And_Each_Only_Their_Department_Unless_Administrator()
    {
        var department = NewDepartment();
        var (user, _) = await CreateAsync(UserRole.User, department);
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (admin, _) = await CreateAsync(UserRole.Admin, null);
        var (member, _) = await CreateAsync(UserRole.User, department);
        var task = await TaskAsync(member, "Al membrului");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/team/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/team/tasks?department={NewDepartment()}")).StatusCode);
        var seenByAdmin = (await admin.GetApiAsync<List<TaskResponse>>($"/api/team/tasks?department={Uri.EscapeDataString(department)}"))!;
        Assert.Contains(seenByAdmin, t => t.Id == task.Id);
        Assert.Empty((await admin.GetApiAsync<List<TaskResponse>>("/api/team/tasks"))!);
    }

    [Fact]
    public async Task A_Manager_Moves_A_Due_Date_And_The_Person_Is_Told()
    {
        var department = NewDepartment();
        var (manager, managerUser) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        var task = await TaskAsync(ana, "Raport lunar", new DateOnly(2026, 12, 3));

        var moved = await MoveAsync(manager, task.Id, "2026-12-10", reason: "Clientul a cerut amânare");

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(new DateOnly(2026, 12, 10), (await moved.Content.ReadApiAsync<TaskResponse>())!.DueDate);
        Assert.Equal(new DateOnly(2026, 12, 10), (await ana.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.DueDate);

        var message = Assert.Single((await ana.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{task.Id}/conversation"))!);
        Assert.Equal(TaskMessageKind.Rescheduled, message.Kind);
        Assert.Equal("Termen: 3 decembrie 2026 → 10 decembrie 2026\nMotiv: Clientul a cerut amânare", message.Text);
        Assert.Equal(managerUser.Id, message.Author!.Id);

        EmailMessage? mail = null;
        for (var i = 0; i < 100 && mail is null; i++)
        {
            mail = factory.Emails.NotificationsTo(anaUser.Email).FirstOrDefault(m => m.Subject.StartsWith("Task replanificat:"));
            if (mail is null) await Task.Delay(30);
        }
        Assert.NotNull(mail);
        Assert.Contains("Termen: 3 decembrie 2026 → 10 decembrie 2026", mail.Body);
        Assert.Contains("Motiv: Clientul a cerut amânare", mail.Body);

        // Removing the due date works too; the same date again changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(manager, task.Id, null)).StatusCode);
        Assert.Null((await ana.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.DueDate);
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(manager, task.Id, null)).StatusCode);
        Assert.Equal(2, (await ana.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{task.Id}/conversation"))!.Count);
    }

    [Fact]
    public async Task Due_Dates_Move_Only_Within_The_Rules()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (user, _) = await CreateAsync(UserRole.User, department);
        var (ana, _) = await CreateAsync(UserRole.User, department);
        var (outsider, _) = await CreateAsync(UserRole.User, NewDepartment());
        var interval = await TaskAsync(ana, "Cu interval", new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 5));
        var foreign = await TaskAsync(outsider, "Din alt departament");
        var done = await TaskAsync(ana, "Terminat");
        await ana.PutApiAsync($"/api/tasks/{done.Id}", new UpdateTaskRequest(done.Title, null, true));

        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(user, interval.Id, "2026-12-12")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(manager, foreign.Id, "2026-12-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(manager, interval.Id, "2026-12-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await MoveAsync(manager, done.Id, "2026-12-12")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(manager, Guid.NewGuid(), "2026-12-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(manager, interval.Id, "1999-01-01")).StatusCode);
        // The reason is required.
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(manager, interval.Id, "2026-12-12", reason: null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(manager, interval.Id, "2026-12-12", reason: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(manager, interval.Id, "2026-12-10", 21)).StatusCode);
    }

    [Fact]
    public async Task A_Manager_Moves_A_Task_Up_Within_Its_Day_And_The_Others_Go_Down()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        var day = new DateOnly(2026, 12, 7);
        var first = await LevelledAsync(ana, "Primul", day, 1);
        var second = await LevelledAsync(ana, "Al doilea", day, 2);
        var third = await LevelledAsync(ana, "Al treilea", day, 3);

        var moved = await MoveAsync(manager, third.Id, "2026-12-07", 1, "Urgent pentru client");

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(1, await LevelAsync(ana, third.Id));
        Assert.Equal(2, await LevelAsync(ana, first.Id));
        Assert.Equal(3, await LevelAsync(ana, second.Id));
        var message = Assert.Single((await ana.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{third.Id}/conversation"))!);
        Assert.Equal("Nivel: 3 → 1\nCoborâte cu un nivel: „Primul”, „Al doilea”\nMotiv: Urgent pentru client", message.Text);

        // Out of the levels: to the end of the day, with no level.
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(manager, third.Id, "2026-12-07", null, "Poate aștepta")).StatusCode);
        Assert.Null(await LevelAsync(ana, third.Id));
    }

    [Fact]
    public async Task A_Manager_Moves_A_Task_To_Another_Day_At_A_Level()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (ana, _) = await CreateAsync(UserRole.User, department);
        var there = await LevelledAsync(ana, "Deja acolo", new DateOnly(2026, 12, 9), 1);
        var task = await LevelledAsync(ana, "De mutat", new DateOnly(2026, 12, 8), 1);

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(manager, task.Id, "2026-12-09", 1, "Mutat după ședință")).StatusCode);

        var after = (await ana.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.Equal(new DateOnly(2026, 12, 9), after.DueDate);
        Assert.Equal(1, after.PriorityLevel);
        Assert.Equal(2, await LevelAsync(ana, there.Id));
        var message = Assert.Single((await ana.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{task.Id}/conversation"))!);
        Assert.Equal("Termen: 8 decembrie 2026 → 9 decembrie 2026\nCoborâte cu un nivel: „Deja acolo”\nMotiv: Mutat după ședință",
            message.Text);
    }

    [Fact]
    public async Task A_Full_Day_Refuses_The_Move_And_Changes_Nothing()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (ana, _) = await CreateAsync(UserRole.User, department);
        var day = new DateOnly(2026, 12, 14);
        for (var level = 1; level <= 20; level++) await LevelledAsync(ana, $"Nivel {level}", day, level);
        var task = await LevelledAsync(ana, "În plus", new DateOnly(2026, 12, 15), 1);

        Assert.Equal(HttpStatusCode.Conflict, (await MoveAsync(manager, task.Id, "2026-12-14", 1)).StatusCode);

        var after = (await ana.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.Equal(new DateOnly(2026, 12, 15), after.DueDate);
        Assert.Equal(1, after.PriorityLevel);
        Assert.Empty((await ana.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{task.Id}/conversation"))!);
    }

    private static async Task<TaskResponse> LevelledAsync(HttpClient client, string title, DateOnly due, int level)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, due, PriorityLevel: level));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }
}
