using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskel.Api.Data;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// An email given from Outlook to several people, with instructions; tasks forwarded with a request to
/// have them back; and where the completion message of an email task goes.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmailDistributionTests(TaskelApiFactory factory)
{
    private const string MessageId = "<offer-77@client.example.com>";

    private static object Email(string sender, IEnumerable<string>? recipients = null, string? instructions = null) => new
    {
        subject = "Cerere ofertă 2027",
        sender = $"Client SRL <{sender}>",
        senderEmail = sender,
        messageId = MessageId,
        body = "Vă rugăm o ofertă.",
        recipients,
        instructions
    };

    private static string NewAddress() => $"client-{Guid.NewGuid():N}@example.com";

    private static async Task<IReadOnlyList<Guid>> DistributeAsync(HttpClient client, object email)
    {
        var response = await client.PostApiAsync("/api/tasks/from-email/distribute", email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<DistributedEmailResponse>())!.TaskIds;
    }

    private static async Task<TaskResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetApiAsync<TaskResponse>($"/api/tasks/{id}"))!;

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid id, string? response) =>
        client.PostApiAsync($"/api/tasks/{id}/complete", new { response });

    private static Task<HttpResponseMessage> ForwardAsync(HttpClient client, Guid id, Guid to, bool returnToMe, string? note = null) =>
        client.PostApiAsync($"/api/tasks/{id}/forward", new { assigneeId = to, note, returnToMe });

    private async Task<IReadOnlyList<EmailMessage>> WaitForAsync(string address, int count)
    {
        for (var i = 0; i < 100 && factory.Emails.NotificationsTo(address).Count < count; i++) await Task.Delay(30);
        return factory.Emails.NotificationsTo(address);
    }

    // ---------- distributing ----------

    [Fact]
    public async Task Each_Recipient_Gets_A_Task_Of_Their_Own_With_The_Instructions()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (_, dan) = await factory.CreateUserAsync();

        var ids = await DistributeAsync(manager,
            Email(NewAddress(), [anaUser.Id.ToString(), dan.Id.ToString()], "Ana: prețurile. Dan: termenele."));

        Assert.Equal(2, ids.Count);
        var task = await GetAsync(ana, ids[0]);
        Assert.Equal("Cerere ofertă 2027", task.Title);
        Assert.Equal(managerUser.Id, task.CreatedBy.Id);
        Assert.Equal(anaUser.Id, task.Assignee!.Id);
        Assert.Equal("Ana: prețurile. Dan: termenele.", task.Instructions);
        Assert.Equal(TaskAccess.Assignee, task.Access);
        Assert.Contains("email", task.Tags);
        Assert.Equal(dan.Id, (await GetAsync(manager, ids[1])).Assignee!.Id);

        var mail = Assert.Single(await WaitForAsync(anaUser.Email, 1));
        Assert.Contains("Mesaj de la", mail.Body);
        Assert.Contains("Ana: prețurile. Dan: termenele.", mail.Body);
        Assert.Single(await WaitForAsync(dan.Email, 1));
    }

    [Fact]
    public async Task The_Due_Date_Chosen_In_Outlook_Is_On_Every_Task()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();

        // As the macro and the add-in send it: the email, the recipients and "dueDate" as yyyy-MM-dd.
        var response = await manager.PostApiAsync("/api/tasks/from-email/distribute", new
        {
            subject = "Cerere ofertă 2027",
            senderEmail = NewAddress(),
            recipients = new[] { anaUser.Id.ToString(), danUser.Id.ToString() },
            dueDate = "2026-12-04"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ids = (await response.Content.ReadApiAsync<DistributedEmailResponse>())!.TaskIds;

        Assert.Equal(new DateOnly(2026, 12, 4), (await GetAsync(ana, ids[0])).DueDate);
        Assert.Equal(new DateOnly(2026, 12, 4), (await GetAsync(dan, ids[1])).DueDate);
    }

    [Theory]
    [InlineData("04.12.2026")]
    [InlineData("1999-12-31")]
    public async Task A_Due_Date_That_Is_Not_A_Date_In_Range_Is_Refused(string dueDate)
    {
        var (manager, _) = await factory.CreateUserAsync();

        var response = await manager.PostApiAsync("/api/tasks/from-email/distribute",
            new { subject = "x", senderEmail = NewAddress(), dueDate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_Recipients_The_Task_Stays_With_The_Sender()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();

        var ids = await DistributeAsync(manager, Email(NewAddress(), instructions: "  De văzut mâine.\r\nApoi sună-l.  "));

        var task = await GetAsync(manager, Assert.Single(ids));
        Assert.Null(task.Assignee);
        Assert.Equal(TaskAccess.Creator, task.Access);
        // Outlook's Windows line endings are stored as the interface's.
        Assert.Equal("De văzut mâine.\nApoi sună-l.", task.Instructions);
        Assert.Equal(managerUser.Id, task.CreatedBy.Id);
    }

    [Fact]
    public async Task A_Recipient_Named_Twice_Gets_One_Task_And_The_Sender_Can_Keep_One_Too()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (_, ana) = await factory.CreateUserAsync();

        var ids = await DistributeAsync(manager,
            Email(NewAddress(), [ana.Id.ToString(), ana.Id.ToString().ToUpperInvariant(), managerUser.Id.ToString()]));

        Assert.Equal(2, ids.Count);
        Assert.Null((await GetAsync(manager, ids[1])).Assignee);
    }

    [Fact]
    public async Task A_Group_Can_Be_A_Recipient()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Admin, null);
        var (member, memberUser) = await factory.CreateUserAsync();
        var created = await manager.PostApiAsync("/api/groups", new SaveGroupRequest("Ofertare " + Guid.NewGuid().ToString("N")[..6], [memberUser.Id]));
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;

        var ids = await DistributeAsync(manager, Email(NewAddress(), [$"group:{group.Id}"]));

        var task = await GetAsync(member, Assert.Single(ids));
        Assert.Equal(group.Id, task.Group!.Id);
        Assert.Equal(TaskAccess.Group, task.Access);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("group:not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("group:00000000-0000-0000-0000-000000000001")]
    public async Task Unknown_Or_Malformed_Recipients_Are_Refused_And_Nothing_Is_Created(string recipient)
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (_, ana) = await factory.CreateUserAsync();

        var response = await manager.PostApiAsync("/api/tasks/from-email/distribute", Email(NewAddress(), [ana.Id.ToString(), recipient]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await manager.GetApiAsync<List<TaskResponse>>("/api/tasks"))!);
    }

    [Fact]
    public async Task At_Most_Fifty_Recipients()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var many = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid().ToString()).ToList();

        var response = await manager.PostApiAsync("/api/tasks/from-email/distribute", Email(NewAddress(), many));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_Single_Task_Endpoint_Does_Not_Take_Recipients()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (_, ana) = await factory.CreateUserAsync();

        var response = await manager.PostApiAsync("/api/tasks/from-email", Email(NewAddress(), [ana.Id.ToString()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Distributing_Works_With_An_Api_Token()
    {
        var (session, _) = await factory.CreateUserAsync();
        var (token, _) = await factory.CreateTokenClientAsync(session);
        var (_, ana) = await factory.CreateUserAsync();

        var ids = await DistributeAsync(token, Email(NewAddress(), [ana.Id.ToString()]));

        Assert.Single(ids);
    }

    [Fact]
    public async Task The_Recipient_List_For_Outlook_Is_Plain_Text_With_Tabs()
    {
        var (session, me) = await factory.CreateUserAsync();
        var (token, _) = await factory.CreateTokenClientAsync(session);

        var response = await token.GetAsync("/api/outlook/recipients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line => Assert.Equal(4, line.Split('\t').Length));
        Assert.Contains($"person\t{me.Id}\t{me.DisplayName}\t{me.Email} (eu)", lines);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/outlook/recipients")).StatusCode);
    }

    // ---------- forwarding with a return ----------

    [Fact]
    public async Task A_Task_Forwarded_With_Return_Comes_Back_Then_Its_Completion_Is_Emailed_To_Whoever_Sent_It_To_Taskel()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var client = NewAddress();
        var id = Assert.Single(await DistributeAsync(manager, Email(client, [anaUser.Id.ToString()], "Te rog să te ocupi.")));

        Assert.Equal(managerUser.Email, (await GetAsync(ana, id)).ReplyTo);
        (await ForwardAsync(ana, id, danUser.Id, returnToMe: true, "Fă tu calculul.")).EnsureSuccessStatusCode();

        // Dan works on it: completing sends it back to Ana, nothing is emailed.
        var forDan = await GetAsync(dan, id);
        Assert.Equal(anaUser.Id, forDan.ReturnsTo!.Id);
        Assert.Null(forDan.ReplyTo);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(dan, id, " ")).StatusCode);
        var back = await CompleteAsync(dan, id, "Calculul e gata: 12.400 lei.");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var afterDan = (await back.Content.ReadApiAsync<TaskResponse>())!;
        Assert.Equal(TaskAccess.Viewer, afterDan.Access);
        Assert.False(afterDan.IsCompleted);
        Assert.Empty(factory.Emails.SentTo(client));
        Assert.Empty(factory.Emails.SentTo(managerUser.Email));

        var mail = Assert.Single(await WaitForAsync(anaUser.Email, 2), m => m.Subject.StartsWith("Task rezolvat"));
        Assert.Contains("Calculul e gata: 12.400 lei.", mail.Body);

        // Back with Ana: open, assigned to her, with Dan's resolution in its path.
        var forAna = await GetAsync(ana, id);
        Assert.Equal(TaskAccess.Assignee, forAna.Access);
        Assert.Equal(TaskState.Open, forAna.State);
        Assert.Null(forAna.ReturnsTo);
        var step = forAna.Forwards[^1];
        Assert.True(step.IsReturn);
        Assert.Equal(danUser.Id, step.From.Id);
        Assert.Equal("Calculul e gata: 12.400 lei.", step.Note);
        Assert.False(forAna.Forwards[^2].ReturnPending);

        // Ana closes it: the message goes to the manager, in the email's thread.
        (await CompleteAsync(ana, id, "Oferta e pregătită, o poți trimite clientului.")).EnsureSuccessStatusCode();
        var reply = Assert.Single(factory.Emails.SentTo(managerUser.Email));
        Assert.Equal(MessageId, reply.InReplyToMessageId);
        Assert.Equal("Cerere ofertă 2027", reply.Subject);
        Assert.Contains(anaUser.DisplayName!, reply.Body);
        Assert.Contains("Oferta e pregătită, o poți trimite clientului.", reply.Body);
        Assert.Empty(factory.Emails.SentTo(client));
        Assert.True((await GetAsync(manager, id)).IsCompleted);
    }

    [Fact]
    public async Task Each_Recipient_Sends_Their_Own_Reply()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var ids = await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString(), danUser.Id.ToString()]));

        (await CompleteAsync(ana, ids[0], "Partea mea e gata.")).EnsureSuccessStatusCode();
        (await CompleteAsync(dan, ids[1], "Și a mea.")).EnsureSuccessStatusCode();

        Assert.Equal(2, factory.Emails.SentTo(managerUser.Email).Count);
    }

    [Fact]
    public async Task Without_The_Return_A_Forwarded_Task_Is_Completed_By_Whoever_Has_It()
    {
        var (manager, managerUser) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var id = Assert.Single(await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString()])));

        (await ForwardAsync(ana, id, danUser.Id, returnToMe: false)).EnsureSuccessStatusCode();
        Assert.Null((await GetAsync(dan, id)).ReturnsTo);
        (await CompleteAsync(dan, id, "Rezolvat de Dan.")).EnsureSuccessStatusCode();

        Assert.True((await GetAsync(manager, id)).IsCompleted);
        Assert.Contains("Rezolvat de Dan.", Assert.Single(factory.Emails.SentTo(managerUser.Email)).Body);
    }

    [Fact]
    public async Task Passed_On_Again_The_Task_Still_Comes_Back_To_Whoever_Asked_For_It()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var (eva, evaUser) = await factory.CreateUserAsync();
        var id = Assert.Single(await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString()])));

        (await ForwardAsync(ana, id, danUser.Id, returnToMe: true)).EnsureSuccessStatusCode();
        (await ForwardAsync(dan, id, evaUser.Id, returnToMe: false)).EnsureSuccessStatusCode();
        Assert.Equal(anaUser.Id, (await GetAsync(eva, id)).ReturnsTo!.Id);
        (await CompleteAsync(eva, id, "Gata.")).EnsureSuccessStatusCode();

        Assert.Equal(anaUser.Id, (await GetAsync(ana, id)).Assignee!.Id);
    }

    [Fact]
    public async Task Two_Requests_For_The_Task_Back_Unwind_One_At_A_Time()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var (eva, evaUser) = await factory.CreateUserAsync();
        var id = Assert.Single(await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString()])));

        (await ForwardAsync(ana, id, danUser.Id, returnToMe: true)).EnsureSuccessStatusCode();
        (await ForwardAsync(dan, id, evaUser.Id, returnToMe: true)).EnsureSuccessStatusCode();

        (await CompleteAsync(eva, id, "Eva: gata.")).EnsureSuccessStatusCode();
        var forDan = await GetAsync(dan, id);
        Assert.Equal(danUser.Id, forDan.Assignee!.Id);
        Assert.Equal(anaUser.Id, forDan.ReturnsTo!.Id);

        (await CompleteAsync(dan, id, "Dan: verificat.")).EnsureSuccessStatusCode();
        var forAna = await GetAsync(ana, id);
        Assert.Equal(anaUser.Id, forAna.Assignee!.Id);
        Assert.Null(forAna.ReturnsTo);
    }

    [Fact]
    public async Task When_The_Creator_Reassigns_Nobody_Waits_For_The_Task_Any_More()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (_, danUser) = await factory.CreateUserAsync();
        var (eva, evaUser) = await factory.CreateUserAsync();
        var id = Assert.Single(await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString()])));
        (await ForwardAsync(ana, id, danUser.Id, returnToMe: true)).EnsureSuccessStatusCode();

        (await manager.PutApiAsync($"/api/tasks/{id}/assignee", new AssignTaskRequest(evaUser.Id))).EnsureSuccessStatusCode();

        Assert.Null((await GetAsync(eva, id)).ReturnsTo);
        (await CompleteAsync(eva, id, "Gata.")).EnsureSuccessStatusCode();
        Assert.True((await GetAsync(manager, id)).IsCompleted);
    }

    [Fact]
    public async Task A_Task_Due_Back_Cannot_Be_Completed_Through_Update()
    {
        var (creator, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var created = await creator.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Raport", null, Priority: TaskPriority.Medium, AssigneeId: anaUser.Id));
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;
        (await ForwardAsync(ana, task.Id, danUser.Id, returnToMe: true)).EnsureSuccessStatusCode();

        var update = await dan.PutApiAsync($"/api/tasks/{task.Id}",
            new { title = "Raport", isCompleted = true, priority = "medium", tags = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        // A task that did not come from an email goes back without a message too.
        (await CompleteAsync(dan, task.Id, null)).EnsureSuccessStatusCode();
        Assert.Equal(anaUser.Id, (await GetAsync(ana, task.Id)).Assignee!.Id);
    }

    [Fact]
    public async Task The_Creator_Completing_An_Email_Task_Still_Replies_To_The_Emails_Sender()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var client = NewAddress();
        var id = Assert.Single(await DistributeAsync(manager, Email(client)));
        Assert.Equal(client, (await GetAsync(manager, id)).ReplyTo);

        (await CompleteAsync(manager, id, "Vă trimitem oferta.")).EnsureSuccessStatusCode();

        Assert.Equal("Vă trimitem oferta.", Assert.Single(factory.Emails.SentTo(client)).Body);
    }

    [Fact]
    public async Task Forwarding_Without_Saying_So_Keeps_The_Old_Behaviour()
    {
        var (manager, _) = await factory.CreateUserAsync();
        var (ana, anaUser) = await factory.CreateUserAsync();
        var (dan, danUser) = await factory.CreateUserAsync();
        var id = Assert.Single(await DistributeAsync(manager, Email(NewAddress(), [anaUser.Id.ToString()])));

        (await ana.PostAsJsonAsync($"/api/tasks/{id}/forward", new { assigneeId = danUser.Id })).EnsureSuccessStatusCode();

        Assert.Null((await GetAsync(dan, id)).ReturnsTo);
        Assert.False((await GetAsync(ana, id)).Forwards[^1].ReturnPending);
    }
}
