using System.Net;
using System.Text;
using Taskel.Api.Controllers;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// The reply that closes an email task: the answer, the history in Taskel for a colleague, the original email quoted,
/// in text and HTML, with the files attached to answers.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReplyHistoryTests(TaskelApiFactory factory)
{
    private const string MessageId = "<cerere-77@client.example.com>";

    private static async Task<TaskResponse> OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static async Task<AttachmentResponse> UploadAsync(HttpClient client, Guid taskId, string name, byte[] content)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", name } };
        var response = await client.PostAsync($"/api/tasks/{taskId}/attachments", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<AttachmentResponse>())!;
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid id, string response, params Guid[] files) =>
        client.PostApiAsync($"/api/tasks/{id}/complete", new { response, attachmentIds = files });

    /// <summary>Ion brings Ana's email from Outlook and gives it to <paramref name="toUserId"/>, with instructions.</summary>
    private static async Task<Guid> EmailFromOutlookAsync(HttpClient ion, Guid? toUserId, string sender) =>
        (await (await ion.PostApiAsync("/api/tasks/from-email/distribute", new
        {
            subject = "Cerere ofertă 2027",
            sender = $"Ana Client <{sender}>",
            senderEmail = sender,
            messageId = MessageId,
            body = "Bună ziua,\nvă rog o ofertă pentru 20 de licențe.",
            recipients = toUserId is null ? Array.Empty<string>() : new[] { toUserId.Value.ToString() },
            instructions = "Răspunde-i până vineri."
        })).Content.ReadApiAsync<DistributedEmailResponse>())!.TaskIds[0];

    private static string NewSender() => $"ana-{Guid.NewGuid():N}@client.example";

    [Fact]
    public async Task A_Colleague_Gets_The_Answer_The_History_And_The_Original_Email_With_The_Answer_Files()
    {
        var (ion, ionUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var sender = NewSender();
        var taskId = await EmailFromOutlookAsync(ion, andreiUser.Id, sender);
        var spec = await UploadAsync(ion, taskId, "specificatii.pdf", "%PDF-1.7 spec"u8.ToArray());
        var offer = await UploadAsync(andrei, taskId, "oferta.pdf", "%PDF-1.7 oferta"u8.ToArray());
        await andrei.PostApiAsync($"/api/tasks/{taskId}/messages", new PostMessageRequest("Am cerut prețurile de la furnizor."));

        var done = await OkAsync(CompleteAsync(andrei, taskId, "Oferta e atașată: 120 lei/licență.", offer.Id));

        Assert.True(done.Attachments.Single(a => a.Id == offer.Id).WithAnswer);
        Assert.False(done.Attachments.Single(a => a.Id == spec.Id).WithAnswer);
        Assert.Empty(factory.Emails.SentTo(sender));
        var reply = Assert.Single(factory.Emails.SentTo(ionUser.Email));
        Assert.Equal(MessageId, reply.InReplyToMessageId);
        Assert.StartsWith($"{andreiUser.DisplayName} a finalizat în Taskel task-ul „Cerere ofertă 2027”:", reply.Body);
        Assert.Contains("Oferta e atașată: 120 lei/licență.", reply.Body);
        Assert.Contains("Istoricul în Taskel", reply.Body);
        Assert.Contains($"{ionUser.DisplayName} a adus emailul în Taskel și i l-a dat lui {andreiUser.DisplayName}.", reply.Body);
        Assert.Contains("Indicații: Răspunde-i până vineri.", reply.Body);
        Assert.Contains("Am cerut prețurile de la furnizor.", reply.Body);
        Assert.Contains("Emailul original", reply.Body);
        Assert.Contains($"> De la: Ana Client <{sender}>", reply.Body);
        Assert.Contains("> vă rog o ofertă pentru 20 de licențe.", reply.Body);
        Assert.Contains("<blockquote", reply.HtmlBody);
        Assert.Contains("Istoricul în Taskel", reply.HtmlBody);

        // Only the file sent with the answer; the email's own files stay out.
        var file = Assert.Single(reply.Attachments!);
        Assert.Equal("oferta.pdf", file.FileName);
        using var content = file.Open()!;
        using var read = new MemoryStream();
        await content.CopyToAsync(read);
        Assert.Equal("%PDF-1.7 oferta"u8.ToArray(), read.ToArray());
    }

    [Fact]
    public async Task Answers_From_A_Distribution_And_Their_Files_Reach_The_Distributor_And_The_Final_Reply()
    {
        var (ion, ionUser) = await factory.CreateUserAsync();
        var (maria, mariaUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var taskId = await EmailFromOutlookAsync(ion, mariaUser.Id, NewSender());
        var parent = await OkAsync(maria.PostApiAsync($"/api/tasks/{taskId}/distribute",
            new { recipients = new[] { andreiUser.Id.ToString() }, message = "Prețurile, te rog." }));
        var copyId = parent.Distributions![0].TaskId;
        var prices = await UploadAsync(andrei, copyId, "preturi.xlsx", "PK prices"u8.ToArray());

        await OkAsync(CompleteAsync(andrei, copyId, "Lista de prețuri e atașată.", prices.Id));

        // The file came with the answer to Maria's task, and shows next to it in the conversation.
        var mine = await maria.GetApiAsync<TaskResponse>($"/api/tasks/{taskId}");
        var received = Assert.Single(mine!.Attachments, a => a.FileName == "preturi.xlsx");
        Assert.True(received.WithAnswer);
        Assert.Equal(HttpStatusCode.OK, (await maria.GetAsync($"/api/tasks/{taskId}/attachments/{received.Id}")).StatusCode);
        var conversation = (await maria.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{taskId}/conversation"))!;
        var answer = Assert.Single(conversation, m => m.Kind == TaskMessageKind.Answered);
        Assert.Equal(received.Id, Assert.Single(answer.Attachments!).Id);
        // Andrei sees it on his copy, as his own file.
        var onCopy = (await andrei.GetApiAsync<List<TaskMessageResponse>>($"/api/tasks/{copyId}/conversation"))!;
        Assert.Equal(prices.Id, Assert.Single(onCopy.Single(m => m.Kind == TaskMessageKind.Answered).Attachments!).Id);

        await OkAsync(CompleteAsync(maria, taskId, "Oferta finală: 120 lei/licență."));

        var reply = Assert.Single(factory.Emails.SentTo(ionUser.Email));
        Assert.Contains($"{mariaUser.DisplayName} a distribuit task-ul către {andreiUser.DisplayName}.", reply.Body);
        Assert.Contains("Lista de prețuri e atașată.", reply.Body);
        Assert.Equal("preturi.xlsx", Assert.Single(reply.Attachments!).FileName);
    }

    [Fact]
    public async Task The_Senders_Reply_Has_No_History_And_Only_The_Files_Attached_Now()
    {
        var (ion, _) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var sender = NewSender();
        // Ion keeps the email for himself, and distributes it.
        var taskId = await EmailFromOutlookAsync(ion, null, sender);
        var parent = await OkAsync(ion.PostApiAsync($"/api/tasks/{taskId}/distribute",
            new { recipients = new[] { andreiUser.Id.ToString() }, message = "Intern: verifică marja." }));
        var copyId = parent.Distributions![0].TaskId;
        var internalFile = await UploadAsync(andrei, copyId, "marja-interna.xlsx", "PK internal"u8.ToArray());
        await OkAsync(CompleteAsync(andrei, copyId, "Marja e 12%.", internalFile.Id));
        var forClient = await UploadAsync(ion, taskId, "oferta-client.pdf", "%PDF-1.7 client"u8.ToArray());

        await OkAsync(CompleteAsync(ion, taskId, "Vă trimitem oferta atașată.", forClient.Id));

        var reply = Assert.Single(factory.Emails.SentTo(sender));
        Assert.DoesNotContain("Istoricul", reply.Body);
        Assert.DoesNotContain("Marja e 12%", reply.Body);
        Assert.DoesNotContain("Intern:", reply.HtmlBody);
        Assert.StartsWith("Vă trimitem oferta atașată.", reply.Body);
        Assert.Contains($"> De la: Ana Client <{sender}>", reply.Body);
        Assert.Equal("oferta-client.pdf", Assert.Single(reply.Attachments!).FileName);
    }

    [Fact]
    public async Task Files_Too_Large_For_An_Email_Are_Named_And_Left_In_Taskel()
    {
        var (ion, ionUser) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var taskId = await EmailFromOutlookAsync(ion, andreiUser.Id, NewSender());
        var big = await UploadAsync(andrei, taskId, "arhiva.zip", new byte[TasksController.MaxReplyAttachmentBytes + 1]);
        var small = await UploadAsync(andrei, taskId, "rezumat.pdf", "%PDF-1.7"u8.ToArray());

        await OkAsync(CompleteAsync(andrei, taskId, "Vezi fișierele.", big.Id, small.Id));

        var reply = Assert.Single(factory.Emails.SentTo(ionUser.Email));
        Assert.Equal("rezumat.pdf", Assert.Single(reply.Attachments!).FileName);
        Assert.Contains("Prea mari pentru email, găsiți-le în Taskel: arhiva.zip", reply.Body);
    }

    [Fact]
    public async Task Only_Files_Of_The_Task_Can_Go_With_Its_Answer()
    {
        var (ion, _) = await factory.CreateUserAsync();
        var (andrei, andreiUser) = await factory.CreateUserAsync();
        var taskId = await EmailFromOutlookAsync(ion, andreiUser.Id, NewSender());
        var other = (await (await andrei.PostApiAsync("/api/tasks", new CreateTaskRequest("Altul", null)))
            .Content.ReadApiAsync<TaskResponse>())!;
        var foreign = await UploadAsync(andrei, other.Id, "strain.pdf", "%PDF"u8.ToArray());

        var response = await CompleteAsync(andrei, taskId, "Gata.", foreign.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void The_Formatted_Reply_Never_Carries_Markup_From_What_People_Wrote()
    {
        var owner = new User { Email = "ion@firma.example", DisplayName = "Ion <b>" };
        var task = new TaskItem { Title = "T", Owner = owner, Description = "<script>alert(1)</script>" };

        var (_, html) = Taskel.Api.Email.ReplyComposer.Compose(task, owner, "<img src=x onerror=alert(1)>", [], true, [], []);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img src=x", html);
        Assert.Contains("Ion &lt;b&gt;", html);
        Assert.Equal(Encoding.UTF8.GetByteCount(html), Encoding.UTF8.GetBytes(html).Length);
    }
}
