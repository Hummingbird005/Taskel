using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// A request that leaves out a value field must be refused, not read as that field's default:
/// a missing role would otherwise demote someone, a missing state would reopen a task.
/// Found by the SonarQube scan (rule S6964, under-posting).
/// </summary>
[Collection(ApiCollection.Name)]
public class UnderPostingTests(TaskelApiFactory factory)
{
    [Fact]
    public async Task Changing_A_Department_Without_A_Role_Does_Not_Demote_Anyone()
    {
        var (admin, adminUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(adminUser.Id, UserRole.Admin, null);
        var (_, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Manager, "Vânzări");

        var response = await admin.PutAsJsonAsync($"/api/admin/users/{managerUser.Id}", new { department = "Suport" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var users = (await admin.GetApiAsync<List<AdminUserResponse>>("/api/admin/users"))!;
        var manager = Assert.Single(users, u => u.Id == managerUser.Id);
        Assert.Equal(UserRole.Manager, manager.Role);
        Assert.Equal("Vânzări", manager.Department);
    }

    [Fact]
    public async Task Saving_A_Task_Without_Its_State_Does_Not_Reopen_It()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var created = await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("Gata", null));
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;
        await alice.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null));

        var response = await alice.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Titlu nou" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.True(stored.IsCompleted);
        Assert.Equal("Gata", stored.Title);
    }

    [Fact]
    public async Task An_Empty_Notification_Setting_Does_Not_Turn_Emails_Off()
    {
        var (bob, _) = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync("/api/notifications", new { })).StatusCode);
        Assert.True((await bob.GetApiAsync<NotificationSettingsResponse>("/api/notifications"))!.EmailOnNewTask);
    }

    [Fact]
    public async Task An_Empty_Attachment_Setting_Is_Refused()
    {
        var (admin, adminUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(adminUser.Id, UserRole.Admin, null);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/settings/attachments", new { })).StatusCode);
    }

    [Fact]
    public async Task Complete_Requests_Still_Work()
    {
        var (admin, adminUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(adminUser.Id, UserRole.Admin, null);
        var (_, other) = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PutApiAsync($"/api/admin/users/{other.Id}", new UpdateUserRequest(UserRole.Manager, "Suport"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/notifications", new { emailOnNewTask = false })).StatusCode);
    }
}
