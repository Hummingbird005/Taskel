using System.Net;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// "Trimite" without completing: the answer goes where completing would send it, stays in the conversation with its
/// files, and the task stays open.
/// </summary>
[Collection(ApiCollection.Name)]
public class SendWithoutCompletingTests(TaskelApiFactory factory)
{
    private const string MessageId = "<intermediar-5@client.example.com>";

    private static async Task<TaskMessageResponse> ReplyAsync(HttpClient client, Guid id, string response, params Guid[] files)
    {
        var answer = await client.PostApiAsync($"/api/tasks/{id}/reply", new { response, attachmentIds = files });
        Assert.True(answer.StatusCode == HttpStatusCode.OK, $"{(int)answer.StatusCode}: {await answer.Content.ReadAsStringAsync()}");
        return (await answer.Content.ReadApiAsync<TaskMessageResponse>())!;
    }

    private static async Task<AttachmentResponse> UploadAsync(HttpClient client, Guid taskId, string name)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.7 x"u8.ToArray()), "file", name } };
        var response = await client.PostAsync($"/api/tasks/{taskId}/attachments", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<AttachmentResponse>())!;
    }

    private static async Task<Guid> EmailAsync(HttpClient ion, Guid? toUserId, string sender) =>
        (await (await ion.PostApiAsync("/api/tasks/from-email/distribute", new
        {
            subject = "Cerere de ofertă",
            sender = $"Ana Client <{sender}>",
            senderEmail = sender,
            messageId = MessageId,
            body = "Bună ziua,\nvă rog o ofertă.",
            recipients = toUserId is null ? Array.Empty<string>() : new[] { toUserId.Value.ToString() }
        })).Content.ReadApiAsync<DistributedEmailResponse>())!.TaskIds[0];

    private static async Task<EmailMessage?> NotificationAsync(TaskelApiFactory factory, string to, string subjectStart)
    {
        for (var i = 0; i < 100; i++)
        {
            var mail = factory.Emails.NotificationsTo(to).FirstOrDefault(m => m.Subject.StartsWith(subjectStart));
            if (mail is not null) return mail;
            await Task.Delay(30);
        }
        return null;
    }

    [Fact]
    public async Task The_Creator_Answers_The_Emails_Sender_And_Keeps_The_Task_Open()
    {
        var (ion, _) = await factory.CreateUserAsync();
        var sender = $"ana-{Guid.NewGuid():N}@client.example";
        var taskId = await EmailAsync(ion, null, sender);
        var file = await UploadAsync(ion, taskId, "intermediar.pdf");

        var message = await ReplyAsync(ion, taskId, "Am primit cererea, revin până vineri.", file.Id);

        Assert.Equal(TaskMessageKind.Replied, message.Kind);
        Assert.Equal(sender, message.SentTo);
        Assert.Equal(file.Id, Assert.Single(message.Attachments!).Id);
        var reply = Assert.Single(factory.Emails.SentTo(sender));
        Assert.Equal(MessageId, reply.InReplyToMessageId);
        Assert.StartsWith("Am primit cererea, revin până vineri.", reply.Body);
        Assert.Equal("intermediar.pdf", Assert.Single(reply.Attachments!).FileName);
        var task = (await ion.GetApiAsync<TaskResponse>($"/api/tasks/{taskId}"))!;
        Assert.False(task.IsCompleted);
        Assert.Equal(TaskState.Open, task.State);
        Assert.Contains((await ion.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{taskId}/conversation"))!,
            m => m.Kind == TaskMessageKind.Replied && m.Text == "Am primit cererea, revin până vineri.");
    }

    [Fact]
    public async Task A_Colleague_Answers_Whoever_Brought_The_Email_And_The_Final_Reply_Keeps_It_In_The_History()
    {
        var (ion, ionUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var taskId = await EmailAsync(ion, andreiUser.Id, $"ana-{Guid.NewGuid():N}@client.example");

        await ReplyAsync(andrei, taskId, "Mai am nevoie de o zi.");

        var first = Assert.Single(factory.Emails.SentTo(ionUser.Email));
        Assert.StartsWith($"{andreiUser.DisplayName} a răspuns în Taskel la task-ul „Cerere de ofertă”, care rămâne deschis:", first.Body);
        Assert.False((await andrei.GetApiAsync<TaskResponse>($"/api/tasks/{taskId}"))!.IsCompleted);

        Assert.Equal(HttpStatusCode.OK,
            (await andrei.PostApiAsync($"/api/tasks/{taskId}/complete", new { response = "Gata: 120 lei." })).StatusCode);
        var last = factory.Emails.SentTo(ionUser.Email)[^1];
        Assert.Contains($"a răspuns pe email către {ionUser.Email}:", last.Body);
        Assert.Contains("Mai am nevoie de o zi.", last.Body);
    }

    [Fact]
    public async Task On_A_Copy_The_Answer_Reaches_The_Distributor_With_Its_Files_And_The_Copy_Stays_Open()
    {
        var (maria, mariaUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var created = await maria.PostApiAsync("/api/tasks", new CreateTaskRequest("Prețuri 2027", null));
        var taskId = (await created.Content.ReadApiAsync<TaskResponse>())!.Id;
        var parent = (await (await maria.PostApiAsync($"/api/tasks/{taskId}/distribute",
            new { recipients = new[] { andreiUser.Id.ToString() }, message = "Prețurile, te rog." })).Content.ReadApiAsync<TaskResponse>())!;
        var copyId = parent.Distributions![0].TaskId;
        var draft = await UploadAsync(andrei, copyId, "draft.xlsx");

        await ReplyAsync(andrei, copyId, "Primul draft, finalul mâine.", draft.Id);

        var copy = (await andrei.GetApiAsync<TaskResponse>($"/api/tasks/{copyId}"))!;
        Assert.False(copy.IsCompleted);
        var onParent = (await maria.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{taskId}/conversation"))!;
        var answer = Assert.Single(onParent, m => m.Kind == TaskMessageKind.Replied);
        Assert.Equal("draft.xlsx", Assert.Single(answer.Attachments!).FileName);
        Assert.Equal(TaskState.Waiting, (await maria.GetApiAsync<TaskResponse>($"/api/tasks/{taskId}"))!.State);
        Assert.NotNull(await NotificationAsync(factory, mariaUser.Email, "Mesaj nou:"));
    }

    [Fact]
    public async Task An_Assignee_Answers_Whoever_Gave_The_Task_And_An_Own_Task_Has_No_One_To_Answer()
    {
        var (maria, mariaUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var given = (await (await maria.PostApiAsync("/api/tasks",
            new CreateTaskRequest("Raport", null, AssigneeId: andreiUser.Id))).Content.ReadApiAsync<TaskResponse>())!;
        var own = (await (await andrei.PostApiAsync("/api/tasks", new CreateTaskRequest("Al meu", null))).Content.ReadApiAsync<TaskResponse>())!;

        await ReplyAsync(andrei, given.Id, "L-am început.");

        Assert.NotNull(await NotificationAsync(factory, mariaUser.Email, "Mesaj nou:"));
        Assert.Contains((await maria.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{given.Id}/conversation"))!,
            m => m.Kind == TaskMessageKind.Replied && m.Author!.Id == andreiUser.Id);
        Assert.Equal(HttpStatusCode.Conflict,
            (await andrei.PostApiAsync($"/api/tasks/{own.Id}/reply", new { response = "Pentru nimeni" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await andrei.PostApiAsync($"/api/tasks/{given.Id}/reply", new { response = "" })).StatusCode);
        // Whoever gave the task has no one to answer either: they write a message instead.
        Assert.Equal(HttpStatusCode.Conflict,
            (await maria.PostApiAsync($"/api/tasks/{given.Id}/reply", new { response = "Nu e al meu" })).StatusCode);
    }
}
