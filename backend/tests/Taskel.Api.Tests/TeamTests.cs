using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class TeamTests(TaskelApiFactory factory)
{
    private static string NewDepartment() => $"Dept {Guid.NewGuid():N}"[..20];
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Task<TeamResponse?> TeamAsync(HttpClient client, string query = "") =>
        client.GetApiAsync<TeamResponse>($"/api/team?today={Today:yyyy-MM-dd}{query}");

    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, DateOnly? due = null,
        TaskPriority priority = TaskPriority.Medium, Guid? assigneeId = null, string? notes = null)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, notes, due, priority, AssigneeId: assigneeId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private async Task<(HttpClient Client, UserResponse User)> CreateAsync(UserRole role, string? department)
    {
        var created = await factory.CreateUserAsync();
        await factory.SetRoleAsync(created.User.Id, role, department);
        return created;
    }

    [Fact]
    public async Task New_Account_Is_A_Plain_User_Without_Department()
    {
        var (client, _) = await factory.CreateUserAsync();

        var me = await client.GetApiAsync<UserResponse>("/api/auth/me");

        Assert.Equal(UserRole.User, me!.Role);
        Assert.Null(me.Department);
        Assert.Contains("\"role\":\"user\"", await client.GetStringAsync("/api/auth/me"));
    }

    [Fact]
    public async Task Plain_User_Cannot_See_Team_Or_Admin_Screens()
    {
        var (client, user) = await CreateAsync(UserRole.User, NewDepartment());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/team")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/team/members/{user.Id}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutApiAsync($"/api/admin/users/{user.Id}", new UpdateUserRequest(UserRole.Admin, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/team")).StatusCode);
    }

    [Fact]
    public async Task Manager_Sees_The_Workload_Of_Their_Department()
    {
        var department = NewDepartment();
        var (manager, managerUser) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department.ToUpperInvariant());
        var (dan, danUser) = await CreateAsync(UserRole.User, department);
        await CreateAsync(UserRole.User, NewDepartment()); // someone else's department

        // Ana: one overdue high-priority task, one due today, one next week, one without date.
        await CreateAsync(ana, "Overdue", Today.AddDays(-2), TaskPriority.High);
        await CreateAsync(ana, "Today", Today);
        await CreateAsync(ana, "Next week", Today.AddDays(5));
        await CreateAsync(ana, "Someday");
        // Dan: completed work. One finished now, one 10 days ago, one 40 days ago.
        foreach (var daysAgo in new[] { 0, 10, 40 })
        {
            var task = await CreateAsync(dan, $"Done {daysAgo}");
            await dan.PostApiAsync($"/api/tasks/{task.Id}/complete", new { });
            if (daysAgo > 0) await factory.SetCompletedAtAsync(task.Id, DateTime.UtcNow.AddDays(-daysAgo));
        }
        // A task the manager hands to Dan counts for Dan, not for the manager.
        await CreateAsync(manager, "Delegated to Dan", Today.AddDays(-1), assigneeId: danUser.Id);

        var team = (await TeamAsync(manager))!;

        Assert.Equal(department, team.Department, ignoreCase: true);
        Assert.Equal(3, team.Members.Count);
        var byId = team.Members.ToDictionary(m => m.User.Id, m => m.Stats);
        Assert.Equal(new TeamStats(Open: 4, Overdue: 1, DueToday: 1, DueNext7Days: 1, HighPriorityOpen: 1, CompletedLast7Days: 0, CompletedLast30Days: 0), byId[anaUser.Id]);
        Assert.Equal(new TeamStats(Open: 1, Overdue: 1, DueToday: 0, DueNext7Days: 0, HighPriorityOpen: 0, CompletedLast7Days: 1, CompletedLast30Days: 2), byId[danUser.Id]);
        Assert.Equal(TeamStats.Empty, byId[managerUser.Id]);
        Assert.Equal(new TeamStats(5, 2, 1, 1, 1, 1, 2), team.Totals);
        // Busiest person first.
        Assert.Equal(anaUser.Id, team.Members[0].User.Id);
        Assert.Equal(UserRole.Manager, team.Members.Single(m => m.User.Id == managerUser.Id).Role);
    }

    [Fact]
    public async Task Manager_Reads_A_Members_Tasks_In_Full_But_Cannot_Change_Them()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        var task = await CreateAsync(ana, "Ana's task", notes: "private notes");

        var tasks = (await manager.GetApiAsync<List<TaskResponse>>($"/api/team/members/{anaUser.Id}/tasks"))!;

        var seen = Assert.Single(tasks);
        Assert.Equal("Ana's task", seen.Title);
        Assert.Equal("private notes", seen.Description);
        // Reading is all a manager gets: the task itself stays out of reach.
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Changed", null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.DoesNotContain((await manager.GetApiAsync<List<TaskResponse>>("/api/tasks"))!, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Manager_Cannot_Look_Into_Another_Department()
    {
        var mine = NewDepartment();
        var theirs = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, mine);
        var (_, outsider) = await CreateAsync(UserRole.User, theirs);
        var (_, noDepartment) = await CreateAsync(UserRole.User, null);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/team?department={Uri.EscapeDataString(theirs)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/team/members/{outsider.Id}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/team/members/{noDepartment.Id}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/team/members/{Guid.NewGuid()}/tasks")).StatusCode);

        var team = (await TeamAsync(manager))!;
        Assert.Equal([mine], team.Departments);
        Assert.DoesNotContain(team.Members, m => m.User.Id == outsider.Id);
    }

    [Fact]
    public async Task Manager_Without_A_Department_Sees_Nobody()
    {
        var (manager, _) = await CreateAsync(UserRole.Manager, null);

        var team = (await TeamAsync(manager))!;

        Assert.Null(team.Department);
        Assert.Empty(team.Members);
        Assert.Empty(team.Departments);
    }

    [Fact]
    public async Task Administrator_Can_Look_At_Any_Department()
    {
        var department = NewDepartment();
        var (admin, _) = await CreateAsync(UserRole.Admin, null);
        var (ana, anaUser) = await CreateAsync(UserRole.User, department);
        await CreateAsync(ana, "Task");

        var team = (await TeamAsync(admin, $"&department={Uri.EscapeDataString(department)}"))!;

        Assert.Equal(anaUser.Id, Assert.Single(team.Members).User.Id);
        Assert.Contains(department, team.Departments);
        Assert.Single((await admin.GetApiAsync<List<TaskResponse>>($"/api/team/members/{anaUser.Id}/tasks"))!);
    }

    [Fact]
    public async Task Role_Changes_Take_Effect_Without_Signing_In_Again()
    {
        var department = NewDepartment();
        var (client, user) = await CreateAsync(UserRole.Manager, department);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/team")).StatusCode);

        await factory.SetRoleAsync(user.Id, UserRole.User, department);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/team")).StatusCode);
    }

    [Theory]
    [InlineData("?today=not-a-date")]
    [InlineData("?department=a%00b")]
    public async Task Invalid_Query_Is_Rejected(string query)
    {
        var (manager, _) = await CreateAsync(UserRole.Manager, NewDepartment());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/team" + query)).StatusCode);
    }

    [Fact]
    public async Task A_Date_Far_From_The_Servers_Is_Ignored()
    {
        var department = NewDepartment();
        var (manager, _) = await CreateAsync(UserRole.Manager, department);
        await CreateAsync(manager, "Due today", Today);

        // A caller claiming it is next year cannot make everything look overdue.
        var team = await manager.GetApiAsync<TeamResponse>($"/api/team?today={Today.AddYears(1):yyyy-MM-dd}");

        Assert.Equal(0, team!.Totals.Overdue);
    }

    // ---------- administration ----------

    [Fact]
    public async Task Administrator_Sets_Roles_And_Departments()
    {
        var (admin, adminUser) = await CreateAsync(UserRole.Admin, null);
        var (client, user) = await factory.CreateUserAsync();
        var department = NewDepartment();

        var listed = (await admin.GetApiAsync<List<AdminUserResponse>>("/api/admin/users"))!;
        Assert.Contains(listed, u => u.Id == user.Id && u.Role == UserRole.User && u.Department is null);
        Assert.DoesNotContain("password", await admin.GetStringAsync("/api/admin/users"), StringComparison.OrdinalIgnoreCase);

        var updated = await admin.PutApiAsync($"/api/admin/users/{user.Id}", new UpdateUserRequest(UserRole.Manager, $"  {department}  "));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var me = await client.GetApiAsync<UserResponse>("/api/auth/me");
        Assert.Equal(UserRole.Manager, me!.Role);
        Assert.Equal(department, me.Department);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/team")).StatusCode);

        // Clearing the department.
        await admin.PutApiAsync($"/api/admin/users/{user.Id}", new UpdateUserRequest(UserRole.Manager, " "));
        Assert.Null((await client.GetApiAsync<UserResponse>("/api/auth/me"))!.Department);

        // An administrator may change their own department, but not their own role.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutApiAsync($"/api/admin/users/{adminUser.Id}", new UpdateUserRequest(UserRole.Admin, department))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutApiAsync($"/api/admin/users/{adminUser.Id}", new UpdateUserRequest(UserRole.User, department))).StatusCode);
        Assert.Equal(UserRole.Admin, (await admin.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);
    }

    public static TheoryData<string> InvalidUserUpdates => ApiJson.Bodies(
    [
        new { role = "owner", department = "X" },
        new { role = 2, department = "X" },
        new { role = "manager", department = new string('d', 101) },
        new { role = "manager", department = "a\u0007b" },
        new { role = "manager", department = "X", isAdmin = true },
        new { department = "X", role = (string?)null },
    ]);

    [Theory]
    [MemberData(nameof(InvalidUserUpdates))]
    public async Task Invalid_User_Update_Is_Rejected(string body)
    {
        var (admin, _) = await CreateAsync(UserRole.Admin, null);
        var (_, user) = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutApiAsync($"/api/admin/users/{user.Id}", ApiJson.Element(body))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutApiAsync($"/api/admin/users/{Guid.NewGuid()}", new UpdateUserRequest(UserRole.User, null))).StatusCode);
    }

    [Fact]
    public async Task Configured_Administrator_Is_Always_An_Administrator()
    {
        var (client, user) = await factory.CreateUserAsync();
        var (otherAdmin, _) = await CreateAsync(UserRole.Admin, null);
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Auth:AdminEmails:0", user.Email.ToUpperInvariant()));

        // Same account and session, on a server that names it as administrator.
        var cookie = (await client.GetAsync("/api/auth/me")).RequestMessage!.Headers.GetValues("Cookie").First();
        var asAdmin = configured.CreateClient();
        asAdmin.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(UserRole.Admin, (await asAdmin.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);
        Assert.Equal(HttpStatusCode.OK, (await asAdmin.GetAsync("/api/admin/users")).StatusCode);

        // Another administrator cannot take the role away there; the listing says why.
        var other = configured.CreateClient();
        other.DefaultRequestHeaders.Add("Cookie",
            (await otherAdmin.GetAsync("/api/auth/me")).RequestMessage!.Headers.GetValues("Cookie").First());
        var result = await other.PutApiAsync($"/api/admin/users/{user.Id}", new UpdateUserRequest(UserRole.User, "Ops"));
        var saved = (await result.Content.ReadApiAsync<AdminUserResponse>())!;
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.True(saved.RoleIsFixed);
        Assert.Equal("Ops", saved.Department);

        // Without the setting, the account is an ordinary user again.
        Assert.Equal(UserRole.User, (await client.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);
    }

    // ---------- directory sync ----------

    private async Task<UserResponse> DirectorySignInAsync(string name, string password = "Domain-Pass-1")
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login/directory", new DirectoryLoginRequest(name, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadApiAsync<UserResponse>())!;
    }

    [Fact]
    public async Task Department_And_Manager_Role_Come_From_The_Directory()
    {
        var name = $"u{Guid.NewGuid():N}"[..16];
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1", department: "Vânzări", isManager: true);

        var first = await DirectorySignInAsync(name);
        Assert.Equal("Vânzări", first.Department);
        Assert.Equal(UserRole.Manager, first.Role);

        // Moved to another department and out of the managers group.
        factory.Directory.Replace(name, "Domain-Pass-1", account with { Department = "Suport", IsManager = false });
        var second = await DirectorySignInAsync(name);
        Assert.Equal("Suport", second.Department);
        Assert.Equal(UserRole.User, second.Role);

        // Administrators group wins over everything.
        factory.Directory.Replace(name, "Domain-Pass-1", account with { IsManager = true, IsAdmin = true });
        Assert.Equal(UserRole.Admin, (await DirectorySignInAsync(name)).Role);
        factory.Directory.Replace(name, "Domain-Pass-1", account with { IsManager = true, IsAdmin = false });
        Assert.Equal(UserRole.Manager, (await DirectorySignInAsync(name)).Role);
    }

    [Fact]
    public async Task What_The_Directory_Does_Not_Say_Is_Left_As_Set_In_Taskel()
    {
        var name = $"u{Guid.NewGuid():N}"[..16];
        factory.Directory.AddAccount(name, "Domain-Pass-1"); // no department, no groups configured
        var user = await DirectorySignInAsync(name);
        await factory.SetRoleAsync(user.Id, UserRole.Manager, "Set by hand");

        var again = await DirectorySignInAsync(name);

        Assert.Equal(UserRole.Manager, again.Role);
        Assert.Equal("Set by hand", again.Department);
    }
}
