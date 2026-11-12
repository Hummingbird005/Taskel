using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class AssignmentTests(TaskelApiFactory factory)
{
    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, Guid? assigneeId = null)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, AssigneeId: assigneeId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<List<TaskResponse>> ListAsync(HttpClient client) =>
        (await client.GetApiAsync<List<TaskResponse>>("/api/tasks"))!;

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid taskId, Guid? assigneeId) =>
        client.PutApiAsync($"/api/tasks/{taskId}/assignee", new AssignTaskRequest(assigneeId));

    [Fact]
    public async Task New_Task_Belongs_To_Its_Creator_With_No_Assignee()
    {
        var (client, user) = await factory.CreateUserAsync();

        var task = await CreateAsync(client, "Mine");

        Assert.Equal(user.Id, task.CreatedBy.Id);
        Assert.Equal(user.Email, task.CreatedBy.Email);
        Assert.Null(task.Assignee);
    }

    [Fact]
    public async Task Assigned_Task_Is_Visible_To_Creator_And_Assignee_Only()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, _) = await factory.CreateUserAsync();

        var task = await CreateAsync(alice, "For Bob", bobUser.Id);

        Assert.Equal(bobUser.Id, task.Assignee!.Id);
        Assert.Contains(await ListAsync(alice), t => t.Id == task.Id);
        var seenByBob = Assert.Single(await ListAsync(bob));
        Assert.Equal(task.Id, seenByBob.Id);
        Assert.Equal(bobUser.Id, seenByBob.Assignee!.Id);
        Assert.Empty(await ListAsync(carol));
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Assignee_Can_Edit_And_Complete_But_Not_Delete_Or_Reassign()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "For Bob", bobUser.Id);

        var edited = await bob.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Edited by Bob", "notes", false));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(bobUser.Id, (await edited.Content.ReadApiAsync<TaskResponse>())!.Assignee!.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(bob, task.Id, carolUser.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(bob, task.Id, null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new { })).StatusCode);

        var seenByAlice = (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.Equal("Edited by Bob", seenByAlice.Title);
        Assert.True(seenByAlice.IsCompleted);
        Assert.Equal(bobUser.Id, seenByAlice.Assignee!.Id);
    }

    [Fact]
    public async Task Creator_Can_Reassign_Unassign_And_Delete()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Moves around", bobUser.Id);

        var toCarol = await AssignAsync(alice, task.Id, carolUser.Id);
        Assert.Equal(carolUser.Id, (await toCarol.Content.ReadApiAsync<TaskResponse>())!.Assignee!.Id);
        Assert.Empty(await ListAsync(bob));
        Assert.Single(await ListAsync(carol));

        var unassigned = await AssignAsync(alice, task.Id, null);
        Assert.Null((await unassigned.Content.ReadApiAsync<TaskResponse>())!.Assignee);
        Assert.Empty(await ListAsync(carol));

        // Assigning to yourself is the same as keeping it.
        var toSelf = await AssignAsync(alice, task.Id, aliceUser.Id);
        Assert.Null((await toSelf.Content.ReadApiAsync<TaskResponse>())!.Assignee);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Empty(await ListAsync(alice));
    }

    [Fact]
    public async Task Assigning_To_An_Unknown_User_Is_Rejected()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Stays");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("x", null, AssigneeId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(alice, task.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PutAsync($"/api/tasks/{task.Id}/assignee",
                new StringContent("""{"assigneeId":"not-a-guid"}""", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Null((await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Assignee);
    }

    [Fact]
    public async Task Update_Does_Not_Change_The_Assignee()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Keep assignee", bobUser.Id);

        await alice.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Renamed", null, false));

        Assert.Equal(bobUser.Id, (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Assignee!.Id);
    }

    [Fact]
    public async Task Assignee_Completes_An_Email_Task_With_A_Reply()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var sender = $"sender-{Guid.NewGuid():N}@example.com";
        var created = await alice.PostApiAsync("/api/tasks/from-email", new { subject = "Help", senderEmail = sender });
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;
        await AssignAsync(alice, task.Id, bobUser.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await bob.PostApiAsync($"/api/tasks/{task.Id}/complete", new { response = "Rezolvat." })).StatusCode);

        Assert.Equal("Rezolvat.", Assert.Single(factory.Emails.SentTo(sender)).Body);
    }

    [Fact]
    public async Task Users_List_Shows_Names_And_Emails_But_Nothing_Secret()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();

        var json = await alice.GetStringAsync("/api/users");
        var users = (await alice.GetFromJsonAsync<List<UserSummary>>("/api/users"))!;

        Assert.Contains(users, u => u.Id == aliceUser.Id && u.Email == aliceUser.Email);
        Assert.Contains(users, u => u.Id == bobUser.Id && u.DisplayName == bobUser.Email);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("directoryId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Task_Response_Does_Not_Leak_Internal_Fields()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Shape", bobUser.Id);

        var json = await alice.GetStringAsync($"/api/tasks/{task.Id}");

        foreach (var hidden in new[] { "password", "userId", "assigneeId", "owner", "directoryId" })
            Assert.DoesNotContain(hidden, json, StringComparison.OrdinalIgnoreCase);
    }
}
