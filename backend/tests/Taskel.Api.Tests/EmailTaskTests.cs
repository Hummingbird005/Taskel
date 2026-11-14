using System.Net;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class EmailTaskTests(TaskelApiFactory factory)
{
    private async Task<HttpClient> CreateTokenClientAsync() =>
        (await factory.CreateTokenClientAsync(await factory.CreateAuthenticatedClientAsync())).Client;

    private static async Task<TaskItem> PostEmailAsync(HttpClient client, object email)
    {
        var response = await client.PostApiAsync("/api/tasks/from-email", email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskItem>())!;
    }

    [Fact]
    public async Task Email_Becomes_A_Task()
    {
        var client = await CreateTokenClientAsync();

        var task = await PostEmailAsync(client, new
        {
            subject = "  Ofertă   pentru\tproiect  ",
            sender = "Ana Popescu <ana@example.com>",
            receivedAt = "2026-11-10T09:30:00",
            body = "Bună ziua,\r\n\r\n\r\n\r\nVă trimit oferta.\r\nMulțumesc!",
            importance = "high",
            dueDate = "2026-11-20"
        });

        Assert.Equal("Ofertă pentru proiect", task.Title);
        Assert.Equal(
            "De la: Ana Popescu <ana@example.com>\nPrimit: 2026-11-10 09:30\n\nBună ziua,\n\nVă trimit oferta.\nMulțumesc!",
            task.Description);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(new DateOnly(2026, 11, 20), task.DueDate);
        Assert.Equal(["email"], task.Tags);
        Assert.False(task.IsCompleted);

        var stored = (await client.GetApiAsync<TaskItem>($"/api/tasks/{task.Id}"))!;
        Assert.Equal(task.Description, stored.Description);
    }

    [Theory]
    [InlineData("low", TaskPriority.Low)]
    [InlineData("normal", TaskPriority.Medium)]
    [InlineData("high", TaskPriority.High)]
    public async Task Importance_Maps_To_Priority(string importance, TaskPriority expected)
    {
        var task = await PostEmailAsync(await CreateTokenClientAsync(), new { subject = "x", importance });
        Assert.Equal(expected, task.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_Subject_Gets_A_Placeholder_Title(string? subject)
    {
        var task = await PostEmailAsync(await CreateTokenClientAsync(), new { subject });

        Assert.Equal(EmailTaskFactory.NoSubjectTitle, task.Title);
        Assert.Null(task.Description);
        Assert.Equal(TaskPriority.Medium, task.Priority);
    }

    [Fact]
    public async Task Long_Subject_And_Body_Are_Shortened_To_Fit()
    {
        var task = await PostEmailAsync(await CreateTokenClientAsync(), new
        {
            subject = new string('s', 500),
            sender = "someone@example.com",
            body = new string('b', 10_000)
        });

        Assert.Equal(EmailTaskFactory.MaxTitleLength, task.Title.Length);
        Assert.EndsWith("…", task.Title);
        Assert.Equal(EmailTaskFactory.MaxDescriptionLength, task.Description!.Length);
        Assert.StartsWith("De la: someone@example.com\n\nbbb", task.Description);
        Assert.EndsWith("…", task.Description);
    }

    public static TheoryData<string> InvalidEmails => ApiJson.Bodies(
    [
        new { subject = new string('s', 1001) },
        new { subject = "x", body = new string('b', 20_001) },
        new { subject = "x", importance = "urgent" },
        new { subject = "x", receivedAt = "yesterday" },
        new { subject = "x", dueDate = "soon" },
    ]);

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task Invalid_Email_Returns_BadRequest(string email)
    {
        var response = await (await CreateTokenClientAsync()).PostApiAsync("/api/tasks/from-email", ApiJson.Element(email));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_Authentication_And_Also_Works_With_A_Session()
    {
        var email = new { subject = "Hello" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().PostApiAsync("/api/tasks/from-email", email)).StatusCode);

        var session = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal("Hello", (await PostEmailAsync(session, email)).Title);
    }

    [Fact]
    public async Task Email_Task_Belongs_Only_To_The_Token_Owner()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var (aliceToken, _) = await factory.CreateTokenClientAsync(alice);

        var task = await PostEmailAsync(aliceToken, new { subject = "For Alice" });

        Assert.Contains((await alice.GetApiAsync<List<TaskItem>>("/api/tasks"))!, t => t.Id == task.Id);
        Assert.Empty((await bob.GetApiAsync<List<TaskItem>>("/api/tasks"))!);
    }
}
