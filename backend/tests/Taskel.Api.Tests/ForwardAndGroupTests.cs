using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Directory;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class ForwardAndGroupTests(TaskelApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, Guid? assigneeId = null, Guid? groupId = null)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, AssigneeId: assigneeId, GroupId: groupId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<List<TaskResponse>> ListAsync(HttpClient client) =>
        (await client.GetApiAsync<List<TaskResponse>>("/api/tasks"))!;

    private static async Task<TaskResponse> OkTaskAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static Task<HttpResponseMessage> ForwardAsync(HttpClient client, Guid taskId, Guid? userId = null, Guid? groupId = null, string? note = null) =>
        client.PostApiAsync($"/api/tasks/{taskId}/forward", new ForwardTaskRequest(userId, groupId, note));

    private async Task<(HttpClient Client, UserResponse User)> CreateAdminAsync()
    {
        var admin = await factory.CreateUserAsync();
        await factory.SetRoleAsync(admin.User.Id, UserRole.Admin, null);
        return admin;
    }

    /// <summary>An application group made by a fresh administrator.</summary>
    private async Task<GroupDetail> CreateGroupAsync(params Guid[] memberIds)
    {
        var (admin, _) = await CreateAdminAsync();
        var response = await admin.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Echipa"), [.. memberIds]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadApiAsync<GroupDetail>())!;
    }

    // ----- Forwarding -----

    [Fact]
    public async Task Assignee_Forwards_A_Task_And_Keeps_Reading_It()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Ofertă", bobUser.Id);

        var forwarded = await OkTaskAsync(ForwardAsync(bob, task.Id, carolUser.Id, note: "  Ești mai aproape de client.  "));

        Assert.Equal(carolUser.Id, forwarded.Assignee!.Id);
        Assert.Equal(TaskAccess.Viewer, forwarded.Access);
        var step = Assert.Single(forwarded.Forwards);
        Assert.Equal(bobUser.Id, step.From.Id);
        Assert.Equal(carolUser.Id, step.ToUser!.Id);
        Assert.Null(step.ToGroup);
        Assert.Equal("Ești mai aproape de client.", step.Note);

        // Everyone involved sees it, each with their own rights.
        Assert.Equal(TaskAccess.Creator, Assert.Single(await ListAsync(alice), t => t.Id == task.Id).Access);
        Assert.Equal(TaskAccess.Viewer, Assert.Single(await ListAsync(bob)).Access);
        Assert.Equal(TaskAccess.Assignee, Assert.Single(await ListAsync(carol)).Access);
        Assert.Equal(aliceUser.Id, (await carol.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.CreatedBy.Id);
    }

    [Fact]
    public async Task Forwarder_Can_No_Longer_Change_The_Task()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Raport", bobUser.Id);
        await OkTaskAsync(ForwardAsync(bob, task.Id, carolUser.Id));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Schimbat", null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ForwardAsync(bob, task.Id, bobUser.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);

        // The new assignee works on it, and the forwarder sees the outcome.
        await OkTaskAsync(carol.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null)));
        var seen = Assert.Single(await ListAsync(bob));
        Assert.True(seen.IsCompleted);
        Assert.Equal("Raport", seen.Title);
    }

    [Fact]
    public async Task A_Task_Forwarded_Twice_Keeps_Its_Whole_Path()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Contract", bobUser.Id);

        await OkTaskAsync(ForwardAsync(bob, task.Id, carolUser.Id));
        await OkTaskAsync(ForwardAsync(carol, task.Id, danUser.Id));

        var seen = Assert.Single(await ListAsync(dan));
        Assert.Equal(TaskAccess.Assignee, seen.Access);
        Assert.Equal([bobUser.Id, carolUser.Id], seen.Forwards.Select(f => f.From.Id));
        Assert.Equal([carolUser.Id, danUser.Id], seen.Forwards.Select(f => f.ToUser!.Id));
        Assert.Equal(TaskAccess.Viewer, Assert.Single(await ListAsync(bob)).Access);
        Assert.Equal(TaskAccess.Viewer, Assert.Single(await ListAsync(carol)).Access);
    }

    [Fact]
    public async Task A_Task_Forwarded_Back_Is_Workable_Again()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Dus-întors", bobUser.Id);
        await OkTaskAsync(ForwardAsync(bob, task.Id, carolUser.Id));

        await OkTaskAsync(ForwardAsync(carol, task.Id, bobUser.Id));

        Assert.Equal(TaskAccess.Assignee, Assert.Single(await ListAsync(bob)).Access);
        await OkTaskAsync(bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null)));
    }

    [Fact]
    public async Task Forwarding_To_The_Creator_Returns_The_Task_To_Them()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Înapoi", bobUser.Id);

        var forwarded = await OkTaskAsync(ForwardAsync(bob, task.Id, aliceUser.Id));

        Assert.Null(forwarded.Assignee);
        Assert.Equal(aliceUser.Id, Assert.Single(forwarded.Forwards).ToUser!.Id);
        Assert.Equal(TaskAccess.Viewer, Assert.Single(await ListAsync(bob)).Access);
    }

    [Fact]
    public async Task Only_The_Assignee_Can_Forward()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "A lui Bob", bobUser.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await ForwardAsync(alice, task.Id, carolUser.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ForwardAsync(carol, task.Id, carolUser.Id)).StatusCode);
    }

    [Fact]
    public async Task Forward_Needs_Exactly_One_Existing_Target()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(carolUser.Id);
        var task = await CreateAsync(alice, "Țintă", bobUser.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await ForwardAsync(bob, task.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ForwardAsync(bob, task.Id, carolUser.Id, group.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ForwardAsync(bob, task.Id, bobUser.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ForwardAsync(bob, task.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ForwardAsync(bob, task.Id, groupId: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ForwardAsync(bob, task.Id, carolUser.Id, note: new string('x', 501))).StatusCode);

        var unchanged = Assert.Single(await ListAsync(bob));
        Assert.Equal(TaskAccess.Assignee, unchanged.Access);
        Assert.Empty(unchanged.Forwards);
    }

    // ----- Tasks given to a group -----

    [Fact]
    public async Task Group_Task_Is_Seen_By_Every_Member_Until_One_Takes_It()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var (dan, _) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(bobUser.Id, carolUser.Id);

        var task = await CreateAsync(alice, "Pentru echipă", groupId: group.Id);

        Assert.Null(task.Assignee);
        Assert.Equal(group.Id, task.Group!.Id);
        Assert.Equal(GroupKind.App, task.Group.Kind);
        Assert.Equal(TaskAccess.Group, Assert.Single(await ListAsync(bob)).Access);
        Assert.Equal(TaskAccess.Group, Assert.Single(await ListAsync(carol)).Access);
        Assert.Empty(await ListAsync(dan));

        // Nobody works on it before taking it.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Altceva", null, false))).StatusCode);

        var taken = await OkTaskAsync(bob.PostAsync($"/api/tasks/{task.Id}/claim", null));

        Assert.Equal(bobUser.Id, taken.Assignee!.Id);
        Assert.Equal(group.Id, taken.Group!.Id);
        Assert.Equal(TaskAccess.Assignee, taken.Access);
        Assert.Empty(await ListAsync(carol));
        Assert.Equal(HttpStatusCode.NotFound, (await carol.PostAsync($"/api/tasks/{task.Id}/claim", null)).StatusCode);
        await OkTaskAsync(bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null)));
        Assert.True(Assert.Single(await ListAsync(alice), t => t.Id == task.Id).IsCompleted);
    }

    [Fact]
    public async Task A_Taken_Task_Can_Be_Handed_Back_To_The_Group()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(bobUser.Id, carolUser.Id);
        var task = await CreateAsync(alice, "Dat înapoi", groupId: group.Id);
        await OkTaskAsync(bob.PostAsync($"/api/tasks/{task.Id}/claim", null));

        var released = await OkTaskAsync(bob.PostAsync($"/api/tasks/{task.Id}/release", null));

        Assert.Null(released.Assignee);
        Assert.Equal(TaskAccess.Group, released.Access);
        Assert.Equal(TaskAccess.Group, Assert.Single(await ListAsync(carol)).Access);
        await OkTaskAsync(carol.PostAsync($"/api/tasks/{task.Id}/claim", null));
    }

    [Fact]
    public async Task Claim_And_Release_Are_Refused_When_They_Do_Not_Apply()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(bobUser.Id);
        var direct = await CreateAsync(alice, "Direct", bobUser.Id);
        var shared = await CreateAsync(alice, "Comun", groupId: group.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsync($"/api/tasks/{direct.Id}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsync($"/api/tasks/{direct.Id}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsync($"/api/tasks/{shared.Id}/release", null)).StatusCode);
        // The creator is not a member, so cannot take their own group task.
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsync($"/api/tasks/{shared.Id}/claim", null)).StatusCode);
    }

    [Fact]
    public async Task A_Received_Task_Can_Be_Forwarded_To_A_Group()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(carolUser.Id);
        var task = await CreateAsync(alice, "Către grup", bobUser.Id);

        var forwarded = await OkTaskAsync(ForwardAsync(bob, task.Id, groupId: group.Id));

        Assert.Null(forwarded.Assignee);
        Assert.Equal(group.Id, forwarded.Group!.Id);
        Assert.Equal(group.Id, Assert.Single(forwarded.Forwards).ToGroup!.Id);
        Assert.Equal(TaskAccess.Viewer, forwarded.Access);
        Assert.Equal(TaskAccess.Group, Assert.Single(await ListAsync(carol)).Access);
        await OkTaskAsync(carol.PostAsync($"/api/tasks/{task.Id}/claim", null));
        Assert.Equal(TaskAccess.Viewer, Assert.Single(await ListAsync(bob)).Access);
    }

    [Fact]
    public async Task Creator_Can_Move_A_Task_Between_A_Group_A_User_And_Themselves()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(carolUser.Id);
        var task = await CreateAsync(alice, "Mutat", bobUser.Id);

        var onGroup = await OkTaskAsync(alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(GroupId: group.Id)));
        Assert.Null(onGroup.Assignee);
        Assert.Equal(group.Id, onGroup.Group!.Id);
        Assert.Empty(await ListAsync(bob));
        Assert.Single(await ListAsync(carol));

        var onBob = await OkTaskAsync(alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(bobUser.Id)));
        Assert.Null(onBob.Group);
        Assert.Empty(await ListAsync(carol));

        var back = await OkTaskAsync(alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest()));
        Assert.Null(back.Assignee);
        Assert.Null(back.Group);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(bobUser.Id, group.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("x", null, GroupId: Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task An_Unclaimed_Group_Task_Counts_In_Nobodys_Workload()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(bobUser.Id);
        var due = new DateOnly(2031, 3, 10);
        var response = await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("Comun", null, DueDate: due, GroupId: group.Id));
        var task = (await response.Content.ReadApiAsync<TaskResponse>())!;

        async Task<int> CountAsync(Guid userId) =>
            (await alice.GetApiAsync<UserWorkloadResponse>($"/api/users/{userId}/workload?from=2031-03-10&to=2031-03-10"))!.Count;

        Assert.Equal(0, await CountAsync(aliceUser.Id));
        Assert.Equal(0, await CountAsync(bobUser.Id));
        await OkTaskAsync(bob.PostAsync($"/api/tasks/{task.Id}/claim", null));
        Assert.Equal(1, await CountAsync(bobUser.Id));
    }

    // ----- Managing application groups -----

    [Fact]
    public async Task Everyone_Sees_Groups_But_Only_Managers_And_Admins_Make_Them()
    {
        var (user, userInfo) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(userInfo.Id);

        var listed = Assert.Single((await user.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Id == group.Id);
        Assert.Equal(1, listed.MemberCount);
        Assert.False(listed.CanManage);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Al meu"), []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/groups/candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PutApiAsync($"/api/groups/{group.Id}", new SaveGroupRequest(Unique("Furat"), []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/groups")).StatusCode);
    }

    [Fact]
    public async Task Manager_Builds_Groups_From_Their_Own_Department_Only()
    {
        var department = Unique("Dept");
        var (manager, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Manager, department);
        var (_, colleague) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(colleague.Id, UserRole.User, department.ToUpperInvariant());
        var (_, outsider) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(outsider.Id, UserRole.User, Unique("Alt"));

        var candidates = (await manager.GetApiAsync<List<UserSummary>>("/api/groups/candidates"))!;
        Assert.Equal(new[] { managerUser.Id, colleague.Id }.Order(), candidates.Select(c => c.Id).Order());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Mixt"), [colleague.Id, outsider.Id]))).StatusCode);

        var name = Unique("Ai mei");
        var created = await manager.PostApiAsync("/api/groups", new SaveGroupRequest($"  {name}  ", [colleague.Id, colleague.Id]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;
        Assert.Equal(name, group.Name);
        Assert.True(group.CanManage);
        Assert.Equal(colleague.Id, Assert.Single(group.Members).Id);
    }

    [Fact]
    public async Task Manager_Manages_Only_The_Groups_They_Created()
    {
        var department = Unique("Dept");
        var (manager, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Manager, department);
        var (other, otherUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(otherUser.Id, UserRole.Manager, department);
        var created = await manager.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Grup"), [managerUser.Id]));
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;

        Assert.False((await other.GetApiAsync<GroupDetail>($"/api/groups/{group.Id}"))!.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PutApiAsync($"/api/groups/{group.Id}", new SaveGroupRequest(Unique("Alt"), []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);

        var (admin, _) = await CreateAdminAsync();
        Assert.True((await admin.GetApiAsync<GroupDetail>($"/api/groups/{group.Id}"))!.CanManage);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/groups/{group.Id}")).StatusCode);
    }

    [Fact]
    public async Task Updating_A_Group_Changes_Its_Name_And_Members()
    {
        var (admin, _) = await CreateAdminAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var (alice, _) = await factory.CreateUserAsync();
        var created = await admin.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Vechi"), [bobUser.Id]));
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;
        await CreateAsync(alice, "Comun", groupId: group.Id);
        Assert.Single(await ListAsync(bob));

        var newName = Unique("Nou");
        var updated = await admin.PutApiAsync($"/api/groups/{group.Id}", new SaveGroupRequest(newName, [carolUser.Id]));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var detail = (await updated.Content.ReadApiAsync<GroupDetail>())!;
        Assert.Equal(newName, detail.Name);
        Assert.Equal(carolUser.Id, Assert.Single(detail.Members).Id);
        Assert.Empty(await ListAsync(bob));
        Assert.Equal(newName, Assert.Single(await ListAsync(carol)).Group!.Name);
    }

    [Fact]
    public async Task Group_Input_Is_Validated()
    {
        var (admin, _) = await CreateAdminAsync();
        var name = Unique("Unic");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostApiAsync("/api/groups", new SaveGroupRequest(name, []))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostApiAsync("/api/groups", new SaveGroupRequest(name.ToUpperInvariant(), []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostApiAsync("/api/groups", new SaveGroupRequest("", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostApiAsync("/api/groups", new SaveGroupRequest(new string('g', 101), []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostApiAsync("/api/groups", new SaveGroupRequest("Rău\u0001", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Fantome"), [Guid.NewGuid()]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/groups", new { name = Unique("Extra"), memberIds = Array.Empty<Guid>(), kind = "directory" })).StatusCode);
    }

    [Fact]
    public async Task Deleting_A_Group_Returns_Its_Unclaimed_Tasks_To_Their_Creators()
    {
        var (admin, _) = await CreateAdminAsync();
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var created = await admin.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Scurt"), [bobUser.Id]));
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;
        var task = await CreateAsync(alice, "Rămas", groupId: group.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);

        Assert.Empty(await ListAsync(bob));
        var mine = Assert.Single(await ListAsync(alice), t => t.Id == task.Id);
        Assert.Null(mine.Group);
        Assert.Null(mine.Assignee);
    }

    // ----- Active Directory groups -----

    private async Task<(HttpClient Client, UserResponse User)> DirectorySignInAsync(string name)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login/directory", new DirectoryLoginRequest(name, "Domain-Pass-1"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, (await response.Content.ReadApiAsync<UserResponse>())!);
    }

    private static string Account() => $"u{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task Directory_Groups_Arrive_With_The_People_Who_Sign_In()
    {
        var groupName = Unique("AD");
        var (first, second, outsider) = (Account(), Account(), Account());
        factory.Directory.AddAccount(first, "Domain-Pass-1", groups: [groupName]);
        factory.Directory.AddAccount(second, "Domain-Pass-1", groups: [groupName]);
        factory.Directory.AddAccount(outsider, "Domain-Pass-1");

        var (ion, _) = await DirectorySignInAsync(first);
        var listed = Assert.Single((await ion.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Name == groupName);
        Assert.Equal(GroupKind.Directory, listed.Kind);
        Assert.Equal(1, listed.MemberCount);

        var (maria, _) = await DirectorySignInAsync(second);
        var (radu, _) = await DirectorySignInAsync(outsider);
        Assert.Equal(2, Assert.Single((await ion.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Name == groupName).MemberCount);

        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Pentru grupul din AD", groupId: listed.Id);
        Assert.Equal(GroupKind.Directory, task.Group!.Kind);
        Assert.Single(await ListAsync(ion));
        Assert.Single(await ListAsync(maria));
        Assert.Empty(await ListAsync(radu));
        await OkTaskAsync(maria.PostAsync($"/api/tasks/{task.Id}/claim", null));
    }

    [Fact]
    public async Task Leaving_A_Directory_Group_Takes_Effect_At_The_Next_Sign_In()
    {
        var groupName = Unique("AD");
        var name = Account();
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1", groups: [groupName]);
        var (client, _) = await DirectorySignInAsync(name);
        var group = Assert.Single((await client.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Name == groupName);
        var (alice, _) = await factory.CreateUserAsync();
        await CreateAsync(alice, "Comun", groupId: group.Id);
        Assert.Single(await ListAsync(client));

        factory.Directory.Replace(name, "Domain-Pass-1", account with { Groups = [] });
        var (again, _) = await DirectorySignInAsync(name);

        Assert.Empty(await ListAsync(again));
        Assert.Equal(0, Assert.Single((await again.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Id == group.Id).MemberCount);
    }

    [Fact]
    public async Task Directory_Groups_Cannot_Be_Edited_In_Taskel_Even_By_An_Administrator()
    {
        var groupName = Unique("AD");
        var name = Account();
        factory.Directory.AddAccount(name, "Domain-Pass-1", groups: [groupName]);
        var (client, _) = await DirectorySignInAsync(name);
        var group = Assert.Single((await client.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Name == groupName);
        var (admin, _) = await CreateAdminAsync();

        Assert.False((await admin.GetApiAsync<GroupDetail>($"/api/groups/{group.Id}"))!.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutApiAsync($"/api/groups/{group.Id}", new SaveGroupRequest(Unique("Redenumit"), []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        // An application group may share the name of a directory group.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostApiAsync("/api/groups", new SaveGroupRequest(groupName, []))).StatusCode);
    }

    [Fact]
    public async Task When_The_Directory_Reports_No_Groups_Memberships_Are_Left_Alone()
    {
        var groupName = Unique("AD");
        var name = Account();
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1", groups: [groupName]);
        var (client, _) = await DirectorySignInAsync(name);

        factory.Directory.Replace(name, "Domain-Pass-1", account with { Groups = null });
        await DirectorySignInAsync(name);

        Assert.Equal(1, Assert.Single((await client.GetApiAsync<List<GroupListItem>>("/api/groups"))!, g => g.Name == groupName).MemberCount);
    }

    [Theory]
    [InlineData("CN=Vanzari,OU=Echipe,DC=corp,DC=example,DC=com", "Vanzari")]
    [InlineData(@"CN=Vanzari\, Europa,OU=Echipe,DC=corp,DC=example,DC=com", "Vanzari, Europa")]
    [InlineData(@"CN=V\C3\A2nz\C4\83ri,DC=corp", "Vânzări")]
    [InlineData(@"CN=A\+B,DC=corp", "A+B")]
    [InlineData("CN=Fără virgulă", "Fără virgulă")]
    public void Group_Name_Is_Read_From_The_Distinguished_Name(string dn, string expected) =>
        Assert.Equal(expected, DirectoryNames.CommonName(dn));

    [Fact]
    public void Only_Groups_Under_The_Configured_Container_Are_Brought_In()
    {
        string[] memberOf =
        [
            "CN=Suport,OU=Echipe,DC=corp,DC=example,DC=com",
            "cn=suport,ou=echipe,dc=corp,dc=example,dc=com",
            "CN=VPN Users,CN=Users,DC=corp,DC=example,DC=com",
            "   ",
        ];

        Assert.Equal(["Suport", "VPN Users"], DirectoryNames.TaskGroups(memberOf, null).Select(g => g.Name));
        Assert.Equal(["Suport"], DirectoryNames.TaskGroups(memberOf, " ou=Echipe,DC=corp,DC=example,DC=com ").Select(g => g.Name));
    }
}
