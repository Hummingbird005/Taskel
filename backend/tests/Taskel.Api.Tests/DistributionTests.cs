using System.Net;
using Taskel.Api.Email;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Tests;

/// <summary>
/// Distributing a task to several people: their copies, the answers that come back into the task's conversation,
/// sending an answer back, and the reply to the email only when the distributor closes the task.
/// </summary>
[Collection(ApiCollection.Name)]
public class DistributionTests(TaskelApiFactory factory)
{
    private const string MessageId = "<cerere-55@client.example.com>";

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private static async Task<TaskResponse> OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static Task<HttpResponseMessage> DistributeAsync(HttpClient client, Guid taskId, IEnumerable<string> recipients,
        string? message = null, string? dueDate = null) =>
        client.PostApiAsync($"/api/tasks/{taskId}/distribute", new { recipients, message, dueDate });

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid id, string? response, bool closeDistributions = false) =>
        client.PostApiAsync($"/api/tasks/{id}/complete", new { response, closeDistributions });

    private static async Task<TaskResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetApiAsync<TaskResponse>($"/api/tasks/{id}"))!;

    private static async Task<List<TaskMessageResponse>> ConversationAsync(HttpClient client, Guid id) =>
        (await client.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{id}/conversation"))!;

    private async Task<IReadOnlyList<EmailMessage>> WaitForAsync(string address, int count)
    {
        for (var i = 0; i < 100 && factory.Emails.NotificationsTo(address).Count < count; i++) await Task.Delay(30);
        return factory.Emails.NotificationsTo(address);
    }

    /// <summary>An email the manager brought from Outlook and gave to Maria, with a file.</summary>
    private async Task<(HttpClient Manager, UserResponse ManagerUser, HttpClient Maria, UserResponse MariaUser, TaskResponse Task, string Sender)>
        EmailTaskForMariaAsync(string due = "2026-12-10")
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (maria, mariaUser) = await factory.CreateUserAsync();
        var sender = $"client-{Guid.NewGuid():N}@example.com";
        var created = await manager.PostApiAsync("/api/tasks/from-email/distribute", new
        {
            subject = "Cerere ofertă 2027",
            sender = $"Client SRL <{sender}>",
            senderEmail = sender,
            messageId = MessageId,
            body = "Vă rugăm o ofertă pentru 20 de licențe.",
            recipients = new[] { mariaUser.Id.ToString() },
            dueDate = due
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadApiAsync<DistributedEmailResponse>())!.TaskIds[0];
        var file = new ByteArrayContent("%PDF-1.7 oferta"u8.ToArray());
        var uploaded = await manager.PostAsync($"/api/tasks/{id}/attachments", new MultipartFormDataContent { { file, "file", "specificatii.pdf" } });
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        return (manager, managerUser, maria, mariaUser, await GetAsync(maria, id), sender);
    }

    private async Task<GroupDetail> CreateGroupAsync(params Guid[] memberIds)
    {
        var (admin, adminUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(adminUser.Id, UserRole.Admin, null);
        var response = await admin.PostApiAsync("/api/groups", new SaveGroupRequest(Unique("Echipa"), [.. memberIds]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadApiAsync<GroupDetail>())!;
    }

    // ---------- distributing ----------

    [Fact]
    public async Task Each_Recipient_Gets_An_Open_Copy_And_The_Task_Waits_For_Their_Answers()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var (_, elenaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var group = await CreateGroupAsync(danUser.Id);

        var parent = await OkAsync(DistributeAsync(maria, task.Id,
            [andreiUser.Id.ToString(), elenaUser.Id.ToString(), $"group:{group.Id}"], "Andrei: prețurile.\r\nElena: termenele."));

        Assert.Equal(TaskState.Waiting, parent.State);
        Assert.StartsWith(Distributions.WaitingPrefix, parent.WaitingReason);
        Assert.Contains(andreiUser.DisplayName!, parent.WaitingReason);
        Assert.Contains(group.Name, parent.WaitingReason);
        Assert.Equal(3, parent.Distributions!.Count);
        Assert.All(parent.Distributions, d => Assert.Equal(TaskState.Open, d.State));

        var copy = await GetAsync(andrei, parent.Distributions[0].TaskId);
        Assert.Equal(TaskAccess.Assignee, copy.Access);
        Assert.Equal(task.Title, copy.Title);
        Assert.Equal(task.Description, copy.Description);
        Assert.Equal("Andrei: prețurile.\nElena: termenele.", copy.Instructions);
        Assert.Equal(task.DueDate, copy.DueDate);
        Assert.Equal(new ParentTaskSummary(task.Id, task.Title), copy.DistributedFrom);
        Assert.Equal(parent.Assignee!.Id, copy.AnswersTo!.Id);
        // A copy does not reply to the email itself: its answer goes to whoever distributed it.
        Assert.Null(copy.SourceEmailAddress);
        Assert.Null(copy.ReplyTo);
        // The files are shared, and readable from the copy.
        var file = Assert.Single(copy.Attachments);
        Assert.Equal("specificatii.pdf", file.FileName);
        Assert.Equal(HttpStatusCode.OK, (await andrei.GetAsync($"/api/tasks/{copy.Id}/attachments/{file.Id}")).StatusCode);
        // The group's copy waits to be taken, as any group task.
        Assert.Contains(await dan.GetApiAsync<List<TaskResponse>>("/api/tasks") ?? [], t => t.Id == parent.Distributions[2].TaskId);

        var mail = Assert.Single(await WaitForAsync(andreiUser.Email, 1));
        Assert.Contains("ți-a distribuit un task", mail.Body);
        Assert.Contains("Andrei: prețurile.", mail.Body);
        Assert.Single(await WaitForAsync(danUser.Email, 1));
    }

    [Fact]
    public async Task A_Due_Date_Given_When_Distributing_Is_On_The_Copies()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();

        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()], dueDate: "2026-12-01"));

        Assert.Equal(new DateOnly(2026, 12, 1), (await GetAsync(andrei, parent.Distributions![0].TaskId)).DueDate);
        Assert.Equal(new DateOnly(2026, 12, 10), parent.DueDate);
    }

    [Fact]
    public async Task Only_The_Person_Responsible_Distributes_And_Not_To_Themselves()
    {
        var (manager, managerUser, maria, mariaUser, task, _) = await EmailTaskForMariaAsync();
        var (_, andreiUser) = await factory.CreateUserAsync();
        var (stranger, _) = await factory.CreateUserAsync();

        // The manager gave the task to Maria: it is hers to distribute now.
        Assert.Equal(HttpStatusCode.Forbidden, (await DistributeAsync(manager, task.Id, [andreiUser.Id.ToString()])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DistributeAsync(stranger, task.Id, [andreiUser.Id.ToString()])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DistributeAsync(maria, task.Id, [mariaUser.Id.ToString()])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DistributeAsync(maria, task.Id, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DistributeAsync(maria, task.Id, [Guid.NewGuid().ToString()])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DistributeAsync(maria, task.Id, ["group:x"])).StatusCode);

        await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()]));
        // Andrei already has an open copy.
        Assert.Equal(HttpStatusCode.BadRequest, (await DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()])).StatusCode);

        // A creator distributes a task they kept for themselves.
        var own = await manager.PostApiAsync("/api/tasks", new CreateTaskRequest("Al meu", null));
        var ownTask = (await own.Content.ReadApiAsync<TaskResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await DistributeAsync(manager, ownTask.Id, [andreiUser.Id.ToString()])).StatusCode);
        Assert.NotEqual(managerUser.Id, andreiUser.Id);
    }

    // ---------- answers ----------

    [Fact]
    public async Task Answers_Come_Back_And_The_Task_Opens_When_Everyone_Has_Answered()
    {
        var (_, _, maria, mariaUser, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var (elena, elenaUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString(), elenaUser.Id.ToString()], "Vă rog."));
        var andreiCopy = parent.Distributions![0].TaskId;
        var elenaCopy = parent.Distributions[1].TaskId;

        // An answer is required.
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(andrei, andreiCopy, null)).StatusCode);
        // And completing through the edit form is refused.
        var copy = await GetAsync(andrei, andreiCopy);
        var put = await andrei.PutApiAsync($"/api/tasks/{andreiCopy}",
            new UpdateTaskRequest(copy.Title, copy.Description, true, copy.DueDate, copy.Priority, copy.Tags));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);

        await OkAsync(CompleteAsync(andrei, andreiCopy, "Prețurile: 120 lei/licență."));
        var waiting = await GetAsync(maria, task.Id);
        Assert.Equal(TaskState.Waiting, waiting.State);
        Assert.DoesNotContain(andreiUser.DisplayName!, waiting.WaitingReason);
        Assert.Equal("Prețurile: 120 lei/licență.", waiting.Distributions!.Single(d => d.TaskId == andreiCopy).Answer);
        // Maria's first email was the task itself, from the manager.
        var answered = Assert.Single(await WaitForAsync(mariaUser.Email, 2), m => m.Subject.StartsWith("Răspuns primit:"));
        Assert.Contains("Prețurile: 120 lei/licență.", answered.Body);

        await OkAsync(CompleteAsync(elena, elenaCopy, "Livrare în 5 zile."));
        var open = await GetAsync(maria, task.Id);
        Assert.Equal(TaskState.Open, open.State);
        Assert.Null(open.WaitingReason);
        Assert.Contains(await WaitForAsync(mariaUser.Email, 3), m => m.Subject.StartsWith("Toți au răspuns:"));

        var conversation = await ConversationAsync(maria, task.Id);
        Assert.Equal(
            [TaskMessageKind.Distributed, TaskMessageKind.Distributed, TaskMessageKind.Answered, TaskMessageKind.Answered, TaskMessageKind.AllAnswered],
            conversation.Select(m => m.Kind));
        Assert.Equal("Livrare în 5 zile.", conversation[3].Text);
        Assert.Equal(elenaUser.DisplayName, conversation[3].Author!.DisplayName);
        Assert.Equal(elenaCopy, conversation[3].Related!.TaskId);
    }

    [Fact]
    public async Task The_Email_Is_Answered_Only_When_The_Distributor_Closes_The_Task()
    {
        var (_, managerUser, maria, _, task, sender) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()]));

        await OkAsync(CompleteAsync(andrei, parent.Distributions![0].TaskId, "Prețurile atașate."));
        Assert.Empty(factory.Emails.SentTo(sender));
        Assert.Empty(factory.Emails.SentTo(managerUser.Email));

        var closed = await OkAsync(CompleteAsync(maria, task.Id, "Oferta: 120 lei/licență, livrare în 5 zile."));

        Assert.Equal(TaskState.Completed, closed.State);
        // Maria received the email from the manager, who brought it from Outlook: the reply goes to him.
        var reply = Assert.Single(factory.Emails.SentTo(managerUser.Email));
        Assert.Contains("Oferta: 120 lei/licență", reply.Body);
        Assert.Equal(MessageId, reply.InReplyToMessageId);
        Assert.Empty(factory.Emails.SentTo(sender));
    }

    [Fact]
    public async Task An_Answer_Can_Be_Sent_Back_And_The_Task_Waits_Again()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()]));
        var copyId = parent.Distributions![0].TaskId;

        // Not before an answer.
        var early = await maria.PostApiAsync($"/api/tasks/{task.Id}/distributions/{copyId}/send-back", new SendBackRequest("Mai repede."));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        await OkAsync(CompleteAsync(andrei, copyId, "120 lei."));
        Assert.Equal(TaskState.Open, (await GetAsync(maria, task.Id)).State);

        var again = await OkAsync(maria.PostApiAsync($"/api/tasks/{task.Id}/distributions/{copyId}/send-back",
            new SendBackRequest("Lipsește prețul cu TVA.")));

        Assert.Equal(TaskState.Waiting, again.State);
        var copy = await GetAsync(andrei, copyId);
        Assert.Equal(TaskState.Open, copy.State);
        Assert.Null(copy.CompletionResponse);
        Assert.Equal("Lipsește prețul cu TVA.", copy.Instructions);
        Assert.Contains(await WaitForAsync(andreiUser.Email, 2), m => m.Subject.StartsWith("Task trimis înapoi:") && m.Body.Contains("Lipsește prețul cu TVA."));

        // Andrei sees the whole exchange on his copy.
        var exchange = await ConversationAsync(andrei, copyId);
        Assert.Equal([TaskMessageKind.Distributed, TaskMessageKind.Answered, TaskMessageKind.SentBack], exchange.Select(m => m.Kind));

        // The recipient cannot send back, and only the distributor's own copies can be.
        var byRecipient = await andrei.PostApiAsync($"/api/tasks/{task.Id}/distributions/{copyId}/send-back", new SendBackRequest("x"));
        Assert.Equal(HttpStatusCode.NotFound, byRecipient.StatusCode);
        var unrelated = await maria.PostApiAsync($"/api/tasks/{task.Id}/distributions/{Guid.NewGuid()}/send-back", new SendBackRequest("x"));
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);
    }

    [Fact]
    public async Task Closing_Before_Everyone_Answered_Needs_Confirmation_And_Closes_Their_Tasks()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()]));
        var copyId = parent.Distributions![0].TaskId;

        Assert.Equal(HttpStatusCode.Conflict, (await CompleteAsync(maria, task.Id, "Am rezolvat singură.")).StatusCode);
        Assert.Equal(TaskState.Waiting, (await GetAsync(maria, task.Id)).State);

        await OkAsync(CompleteAsync(maria, task.Id, "Am rezolvat singură.", closeDistributions: true));

        var copy = await GetAsync(andrei, copyId);
        Assert.Equal(TaskState.Completed, copy.State);
        Assert.Null(copy.CompletionResponse);
        Assert.Contains(await WaitForAsync(andreiUser.Email, 2), m => m.Subject.StartsWith("Task închis:"));
        Assert.Equal(TaskMessageKind.Closed, (await ConversationAsync(andrei, copyId)).Last().Kind);
    }

    [Fact]
    public async Task Reopening_Or_Deleting_A_Copy_Keeps_The_Task_In_Step()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var (_, elenaUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString(), elenaUser.Id.ToString()]));
        var andreiCopy = parent.Distributions![0].TaskId;
        var elenaCopy = parent.Distributions[1].TaskId;

        await OkAsync(CompleteAsync(andrei, andreiCopy, "Gata."));
        // Maria deletes Elena's copy, the only one open: the task no longer waits.
        Assert.Equal(HttpStatusCode.NoContent, (await maria.DeleteAsync($"/api/tasks/{elenaCopy}")).StatusCode);
        Assert.Equal(TaskState.Open, (await GetAsync(maria, task.Id)).State);

        // Andrei reopens his copy: it is open work again, and the task waits for it.
        var copy = await GetAsync(andrei, andreiCopy);
        await OkAsync(andrei.PutApiAsync($"/api/tasks/{andreiCopy}",
            new UpdateTaskRequest(copy.Title, copy.Description, false, copy.DueDate, copy.Priority, copy.Tags)));
        Assert.Equal(TaskState.Waiting, (await GetAsync(maria, task.Id)).State);

        // The shared file is still there for the task after a copy was deleted.
        var file = task.Attachments.Single();
        Assert.Equal(HttpStatusCode.OK, (await maria.GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_Reason_Written_By_Hand_Is_Never_Replaced()
    {
        var (_, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString()]));
        await OkAsync(maria.PostApiAsync($"/api/tasks/{task.Id}/wait", new WaitTaskRequest("Aștept aprobarea directorului.")));

        await OkAsync(CompleteAsync(andrei, parent.Distributions![0].TaskId, "Gata."));

        var after = await GetAsync(maria, task.Id);
        Assert.Equal(TaskState.Waiting, after.State);
        Assert.Equal("Aștept aprobarea directorului.", after.WaitingReason);
    }

    // ---------- conversation ----------

    [Fact]
    public async Task Each_Recipient_Sees_Only_Their_Own_Exchange()
    {
        var (_, _, maria, mariaUser, task, _) = await EmailTaskForMariaAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var (elena, elenaUser) = await factory.CreateUserAsync();
        var parent = await OkAsync(DistributeAsync(maria, task.Id, [andreiUser.Id.ToString(), elenaUser.Id.ToString()]));
        var andreiCopy = parent.Distributions![0].TaskId;
        var elenaCopy = parent.Distributions[1].TaskId;

        // Andrei asks on his copy; Maria is told and sees it on her task.
        var asked = await andrei.PostApiAsync($"/api/tasks/{andreiCopy}/messages", new PostMessageRequest("Prețul e cu TVA?"));
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        Assert.Contains(await WaitForAsync(mariaUser.Email, 2), m => m.Subject.StartsWith("Mesaj nou:") && m.Body.Contains("Prețul e cu TVA?"));
        // Maria writes to Elena only, and a note for herself.
        Assert.Equal(HttpStatusCode.Created,
            (await maria.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("Elena, grăbește-te.", elenaCopy))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await maria.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("De verificat stocul."))).StatusCode);

        var all = await ConversationAsync(maria, task.Id);
        Assert.Contains(all, m => m.Text == "Prețul e cu TVA?" && m.Related!.TaskId == andreiCopy);
        Assert.Contains(all, m => m.Text == "Elena, grăbește-te." && m.Related!.TaskId == elenaCopy);
        Assert.Contains(all, m => m.Text == "De verificat stocul." && m.Related is null);

        var andreiSees = await ConversationAsync(andrei, andreiCopy);
        Assert.Contains(andreiSees, m => m.Text == "Prețul e cu TVA?");
        Assert.DoesNotContain(andreiSees, m => m.Text == "Elena, grăbește-te.");
        Assert.DoesNotContain(andreiSees, m => m.Text == "De verificat stocul.");
        Assert.Contains(await ConversationAsync(elena, elenaCopy), m => m.Text == "Elena, grăbește-te.");
        Assert.Contains(await WaitForAsync(elenaUser.Email, 2), m => m.Subject.StartsWith("Mesaj nou:"));

        // Andrei cannot read Maria's task, nor write to Elena's copy through it.
        Assert.Equal(HttpStatusCode.NotFound, (await andrei.GetAsync($"/api/tasks/{task.Id}/conversation")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await andrei.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("x", elenaCopy))).StatusCode);
        // A copy of another task is not a target.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await maria.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("x", Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Someone_Who_Only_Reads_A_Task_Cannot_Write_In_Its_Conversation()
    {
        var (manager, _, maria, _, task, _) = await EmailTaskForMariaAsync();
        var (_, andreiUser) = await factory.CreateUserAsync();
        // Maria forwards it on: she can still read it, not write in it.
        await OkAsync(maria.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(andreiUser.Id, null, null)));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await maria.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("Ceva."))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await maria.GetAsync($"/api/tasks/{task.Id}/conversation")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await manager.PostApiAsync($"/api/tasks/{task.Id}/messages", new PostMessageRequest("Notă."))).StatusCode);
    }
}
