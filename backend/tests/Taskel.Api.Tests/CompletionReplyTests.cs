using System.Net;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class CompletionReplyTests(TaskelApiFactory factory)
{
    private static string NewAddress() => $"sender-{Guid.NewGuid():N}@example.com";

    private static async Task<TaskItem> CreateEmailTaskAsync(HttpClient client, string senderEmail,
        string subject = "Offer request", string? messageId = "<original-123@mail.example.com>")
    {
        var response = await client.PostApiAsync("/api/tasks/from-email",
            new { subject, sender = $"Ana Popescu <{senderEmail}>", senderEmail, messageId, body = "Please send the offer." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskItem>())!;
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid id, string? response) =>
        client.PostApiAsync($"/api/tasks/{id}/complete", new { response });

    private static async Task<TaskItem> GetAsync(HttpClient client, Guid id) =>
        (await client.GetApiAsync<TaskItem>($"/api/tasks/{id}"))!;

    [Fact]
    public async Task Completing_An_Email_Task_Sends_The_Response_As_A_Reply()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);
        Assert.Equal(sender, task.SourceEmailAddress);

        var completed = await CompleteAsync(client, task.Id, "  Oferta a fost trimisă. Mulțumim!  ");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var reply = Assert.Single(factory.Emails.SentTo(sender));
        Assert.Equal("Offer request", reply.Subject);
        Assert.Equal("Oferta a fost trimisă. Mulțumim!", reply.Body);
        Assert.Equal("<original-123@mail.example.com>", reply.InReplyToMessageId);

        var stored = await GetAsync(client, task.Id);
        Assert.True(stored.IsCompleted);
        Assert.Equal("Oferta a fost trimisă. Mulțumim!", stored.CompletionResponse);
        Assert.NotNull(stored.CompletedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Email_Task_Cannot_Be_Completed_Without_A_Response(string? response)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);

        var result = await CompleteAsync(client, task.Id, response);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains(sender, await result.Content.ReadAsStringAsync());
        Assert.False((await GetAsync(client, task.Id)).IsCompleted);
        Assert.Empty(factory.Emails.SentTo(sender));
    }

    [Fact]
    public async Task Email_Task_Cannot_Be_Completed_Through_Update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);

        var result = await client.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(task.Title, null, true));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.False((await GetAsync(client, task.Id)).IsCompleted);
        Assert.Empty(factory.Emails.SentTo(sender));
    }

    [Fact]
    public async Task Email_Task_Can_Still_Be_Edited_While_Open()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var task = await CreateEmailTaskAsync(client, NewAddress());

        var result = await client.PutApiAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Renamed", "notes", false, Tags: ["email"]));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var stored = await GetAsync(client, task.Id);
        Assert.Equal("Renamed", stored.Title);
        Assert.Equal(task.SourceEmailAddress, stored.SourceEmailAddress);
    }

    [Fact]
    public async Task Response_Longer_Than_The_Limit_Is_Rejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);

        var result = await CompleteAsync(client, task.Id, new string('x', 4001));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Empty(factory.Emails.SentTo(sender));
    }

    [Fact]
    public async Task When_The_Email_Cannot_Be_Sent_The_Task_Stays_Open()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);

        factory.Emails.FailFor(sender, () => new EmailSendException("rejected", new Exception("smtp")));
        var failed = await CompleteAsync(client, task.Id, "Done");
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var afterFailure = await GetAsync(client, task.Id);
        Assert.False(afterFailure.IsCompleted);
        Assert.Null(afterFailure.CompletionResponse);
        Assert.Null(afterFailure.CompletedAt);

        factory.Emails.StopFailingFor(sender);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, task.Id, "Done")).StatusCode);
        Assert.Single(factory.Emails.SentTo(sender));
    }

    [Fact]
    public async Task When_Email_Is_Not_Configured_The_Task_Stays_Open()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);
        factory.Emails.FailFor(sender, () => new EmailNotConfiguredException());

        var result = await CompleteAsync(client, task.Id, "Done");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.False((await GetAsync(client, task.Id)).IsCompleted);
    }

    [Fact]
    public async Task Completing_Twice_Does_Not_Send_A_Second_Email()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);
        await CompleteAsync(client, task.Id, "First");

        var second = await CompleteAsync(client, task.Id, "Second");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(factory.Emails.SentTo(sender));
        Assert.Equal("First", (await GetAsync(client, task.Id)).CompletionResponse);
    }

    [Fact]
    public async Task Reopened_Task_Needs_A_New_Response_To_Complete_Again()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender);
        await CompleteAsync(client, task.Id, "First");

        var reopened = await client.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(task.Title, null, false));
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var open = await GetAsync(client, task.Id);
        Assert.False(open.IsCompleted);
        Assert.Null(open.CompletedAt);

        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(client, task.Id, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, task.Id, "Second")).StatusCode);
        Assert.Equal(["First", "Second"], factory.Emails.SentTo(sender).Select(m => m.Body));
    }

    [Fact]
    public async Task Manual_Task_Completes_Without_A_Response_And_Sends_Nothing()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Manual", null));
        var task = (await created.Content.ReadApiAsync<TaskItem>())!;
        Assert.Null(task.SourceEmailAddress);

        var viaUpdate = await client.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Manual", null, true));
        Assert.Equal(HttpStatusCode.OK, viaUpdate.StatusCode);
        var stored = await GetAsync(client, task.Id);
        Assert.True(stored.IsCompleted);
        Assert.NotNull(stored.CompletedAt);

        var other = (await (await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Manual 2", null)))
            .Content.ReadApiAsync<TaskItem>())!;
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, other.Id, null)).StatusCode);
        Assert.True((await GetAsync(client, other.Id)).IsCompleted);
    }

    [Fact]
    public async Task Email_Without_A_Usable_Sender_Address_Completes_Like_A_Manual_Task()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostApiAsync("/api/tasks/from-email",
            new { subject = "No address", sender = "Just A Name", senderEmail = "not an address" });
        var task = (await response.Content.ReadApiAsync<TaskItem>())!;

        Assert.Null(task.SourceEmailAddress);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutApiAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(task.Title, null, true))).StatusCode);
    }

    [Fact]
    public async Task Sender_Address_Is_Taken_From_The_Display_Form_When_Not_Given_Separately()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();

        var response = await client.PostApiAsync("/api/tasks/from-email",
            new { subject = "Old macro", sender = $"Ana Popescu <{sender}>" });

        Assert.Equal(sender, (await response.Content.ReadApiAsync<TaskItem>())!.SourceEmailAddress);
    }

    [Theory]
    [InlineData("no-brackets@mail.example.com")]
    [InlineData("<id@host>\r\nBcc: victim@example.com")]
    [InlineData("<two@ids> <second@id>")]
    [InlineData("")]
    public async Task Malformed_Message_Id_Is_Dropped(string messageId)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(client, sender, messageId: messageId);

        await CompleteAsync(client, task.Id, "Done");

        Assert.Null(Assert.Single(factory.Emails.SentTo(sender)).InReplyToMessageId);
    }

    [Fact]
    public async Task Another_User_Cannot_Complete_The_Task_Or_Trigger_The_Email()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var sender = NewAddress();
        var task = await CreateEmailTaskAsync(alice, sender);

        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(bob, task.Id, "Hijack")).StatusCode);
        Assert.Empty(factory.Emails.SentTo(sender));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(factory.CreateClient(), task.Id, "x")).StatusCode);
    }

    [Fact]
    public async Task Reply_Headers_Are_Not_Exposed_In_The_Api()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var task = await CreateEmailTaskAsync(client, NewAddress());

        var json = await client.GetStringAsync($"/api/tasks/{task.Id}");

        Assert.Contains("sourceEmailAddress", json);
        Assert.DoesNotContain("original-123", json);
        Assert.DoesNotContain("sourceEmailSubject", json);
        Assert.DoesNotContain("requiresCompletionReply", json);
    }
}
