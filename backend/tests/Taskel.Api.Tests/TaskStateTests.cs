using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class TaskStateTests(TaskelApiFactory factory)
{
    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, Guid? assigneeId = null)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, AssigneeId: assigneeId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<TaskResponse> OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static Task<HttpResponseMessage> WaitAsync(HttpClient client, Guid id, string reason) =>
        client.PostApiAsync($"/api/tasks/{id}/wait", new WaitTaskRequest(reason));

    [Fact]
    public async Task A_New_Task_Is_Open()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Nou");

        Assert.Equal(TaskState.Open, task.State);
        Assert.Null(task.WaitingReason);
        Assert.Null(task.WaitingSince);
    }

    [Fact]
    public async Task A_Task_Goes_On_Hold_With_A_Reason_And_Back()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Contract");

        var waiting = await OkAsync(WaitAsync(alice, task.Id, "  Aștept avizul juridic.  "));
        Assert.Equal(TaskState.Waiting, waiting.State);
        Assert.Equal("Aștept avizul juridic.", waiting.WaitingReason);
        Assert.NotNull(waiting.WaitingSince);
        Assert.False(waiting.IsCompleted);
        Assert.Equal(TaskState.Waiting, (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.State);

        // A new reason replaces the old one; it has been waiting since the first time.
        var again = await OkAsync(WaitAsync(alice, task.Id, "Aștept și semnătura directorului."));
        Assert.Equal("Aștept și semnătura directorului.", again.WaitingReason);
        Assert.Equal(waiting.WaitingSince, again.WaitingSince);

        var resumed = await OkAsync(alice.PostAsync($"/api/tasks/{task.Id}/resume", null));
        Assert.Equal(TaskState.Open, resumed.State);
        Assert.Null(resumed.WaitingReason);
        Assert.Null(resumed.WaitingSince);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsync($"/api/tasks/{task.Id}/resume", null)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_Reason_Is_Required(string reason)
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Fără motiv");

        Assert.Equal(HttpStatusCode.BadRequest, (await WaitAsync(alice, task.Id, reason)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/tasks/{task.Id}/wait", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await WaitAsync(alice, task.Id, new string('x', 1001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await WaitAsync(alice, task.Id, "rău\u0001")).StatusCode);
        Assert.Equal(TaskState.Open, (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.State);
    }

    [Fact]
    public async Task Completing_A_Task_On_Hold_Takes_It_Off_Hold_And_Keeps_The_Resolution()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Rezolvat");
        await OkAsync(WaitAsync(alice, task.Id, "Aștept piesa."));

        var done = await OkAsync(alice.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest("Piesa a sosit și a fost montată.")));

        Assert.Equal(TaskState.Completed, done.State);
        Assert.True(done.IsCompleted);
        Assert.Equal("Piesa a sosit și a fost montată.", done.CompletionResponse);
        Assert.Null(done.WaitingReason);
        Assert.Null(done.WaitingSince);
    }

    [Fact]
    public async Task A_Completed_Task_Cannot_Go_On_Hold_Until_It_Is_Reopened()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Gata");
        await OkAsync(alice.PostApiAsync($"/api/tasks/{task.Id}/complete", new CompleteTaskRequest(null)));

        Assert.Equal(HttpStatusCode.Conflict, (await WaitAsync(alice, task.Id, "Motiv")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsync($"/api/tasks/{task.Id}/resume", null)).StatusCode);

        var reopened = await OkAsync(alice.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Gata", null, false)));
        Assert.Equal(TaskState.Open, reopened.State);
        Assert.Equal(TaskState.Waiting, (await OkAsync(WaitAsync(alice, task.Id, "Motiv"))).State);
    }

    [Fact]
    public async Task Completing_Through_An_Edit_Also_Takes_It_Off_Hold()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Prin editare");
        await OkAsync(WaitAsync(alice, task.Id, "Aștept."));

        var done = await OkAsync(alice.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Prin editare", null, true)));

        Assert.Equal(TaskState.Completed, done.State);
        Assert.Null(done.WaitingReason);
    }

    [Fact]
    public async Task Editing_A_Task_On_Hold_Keeps_It_On_Hold()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Titlu vechi");
        await OkAsync(WaitAsync(alice, task.Id, "Aștept."));

        var edited = await OkAsync(alice.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Titlu nou", "Detalii", false)));

        Assert.Equal(TaskState.Waiting, edited.State);
        Assert.Equal("Aștept.", edited.WaitingReason);
    }

    [Fact]
    public async Task Only_The_People_Working_On_It_Change_Its_State_And_Everyone_Who_Sees_It_Reads_The_Reason()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var (dan, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Pentru Bob", bobUser.Id);

        // The assignee and the creator may.
        await OkAsync(WaitAsync(bob, task.Id, "Aștept datele de la client."));
        Assert.Equal("Aștept datele de la client.", (await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.WaitingReason);
        await OkAsync(alice.PostAsync($"/api/tasks/{task.Id}/resume", null));

        // Someone who forwarded it can read it but not change it; outsiders do not see it.
        await bob.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(carolUser.Id));
        await OkAsync(WaitAsync(carol, task.Id, "Aștept aprobarea."));
        Assert.Equal("Aștept aprobarea.", (await bob.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.WaitingReason);
        Assert.Equal(HttpStatusCode.Forbidden, (await WaitAsync(bob, task.Id, "Altceva")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync($"/api/tasks/{task.Id}/resume", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await WaitAsync(dan, task.Id, "Altceva")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dan.PostAsync($"/api/tasks/{task.Id}/resume", null)).StatusCode);
    }

    [Fact]
    public async Task A_Task_On_Hold_Still_Counts_As_Open_Work()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var response = await alice.PostApiAsync("/api/tasks", new CreateTaskRequest("În calendar", null, DueDate: new DateOnly(2032, 5, 4)));
        var task = (await response.Content.ReadApiAsync<TaskResponse>())!;
        await OkAsync(WaitAsync(alice, task.Id, "Aștept."));

        var workload = await alice.GetApiAsync<UserWorkloadResponse>($"/api/users/{aliceUser.Id}/workload?from=2032-05-04&to=2032-05-04");
        Assert.Equal(1, workload!.Count);
    }
}
