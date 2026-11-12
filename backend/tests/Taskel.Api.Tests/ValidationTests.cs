using System.Net;
using System.Net.Http.Json;
using System.Text;
using Taskel.Api.Models;
using Taskel.Api.Validation;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class ValidationTests(TaskelApiFactory factory)
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, string title = "Valid")
    {
        var created = await client.PostApiAsync("/api/tasks", new { title });
        return (await created.Content.ReadApiAsync<TaskResponse>())!;
    }

    // Raw JSON, so escapes such as \u0000 reach the API exactly as written.
    public static TheoryData<string, string> InvalidTaskBodies => new()
    {
        { "NUL in title", """{"title":"a\u0000b"}""" },
        { "line break in title", """{"title":"line one\nline two"}""" },
        { "escape character in title", """{"title":"a\u001bb"}""" },
        { "unpaired surrogate in title", """{"title":"a\ud83db"}""" },
        { "blank title", """{"title":"   "}""" },
        { "missing title", """{"priority":"low"}""" },
        { "null title", """{"title":null}""" },
        { "title too long", $$"""{"title":"{{new string('x', 201)}}"}""" },
        { "NUL in notes", """{"title":"x","description":"a\u0000b"}""" },
        { "bell in notes", """{"title":"x","description":"a\u0007b"}""" },
        { "notes too long", $$"""{"title":"x","description":"{{new string('x', 2001)}}"}""" },
        { "due date far in the future", """{"title":"x","dueDate":"2101-01-01"}""" },
        { "due date far in the past", """{"title":"x","dueDate":"1999-12-31"}""" },
        { "due date with five-digit year", """{"title":"x","dueDate":"20266-01-01"}""" },
        { "comma in tag", """{"title":"x","tags":["a,b"]}""" },
        { "NUL in tag", """{"title":"x","tags":["a\u0000b"]}""" },
        { "tags not a list", """{"title":"x","tags":"work"}""" },
        { "unknown property", """{"title":"x","colour":"red"}""" },
        { "misspelt property", """{"title":"x","duedate2":"2027-01-01"}""" },
        { "title is a number", """{"title":5}""" },
        { "body is an array", """[{"title":"x"}]""" },
        { "body is not JSON", "title=x" },
        { "empty body", "" },
    };

    [Theory]
    [MemberData(nameof(InvalidTaskBodies))]
    public async Task Invalid_Task_Is_Rejected_On_Create_And_Update(string reason, string body)
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsync("/api/tasks", Json(body));
        Assert.True(created.StatusCode == HttpStatusCode.BadRequest, $"create: {reason} gave {(int)created.StatusCode}");

        var task = await CreateTaskAsync(client);
        var updated = await client.PutAsync($"/api/tasks/{task.Id}", Json(body));
        Assert.True(updated.StatusCode == HttpStatusCode.BadRequest, $"update: {reason} gave {(int)updated.StatusCode}");
        Assert.Equal("Valid", (await client.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Title);
    }

    [Fact]
    public async Task Valid_Text_Is_Kept_As_Written()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsync("/api/tasks", Json(
            """{"title":"  Ședință 😀 «proiect»  ","description":"Rând 1\n\tRând 2\r\nRând 3","dueDate":"2100-12-31","tags":["Șantier 2"]}"""));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;
        Assert.Equal("Ședință 😀 «proiect»", task.Title);
        Assert.Equal("Rând 1\n\tRând 2\r\nRând 3", task.Description);
        Assert.Equal(["șantier 2"], task.Tags);
    }

    [Fact]
    public async Task Blank_Notes_Are_Stored_As_None()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostApiAsync("/api/tasks", new { title = "x", description = "  \n " });
        Assert.Null((await created.Content.ReadApiAsync<TaskResponse>())!.Description);
    }

    public static TheoryData<string> InvalidEmails => new()
    {
        "plainaddress", "a@b", "a@b.c", "two@@example.com", "spa ce@example.com", "a@exam ple.com",
        "Name <a@example.com>", "a@example.com,b@example.com", "a@example..com", "@example.com", "a@",
        "a\u0000@example.com", new string('a', 250) + "@example.com",
    };

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task Registration_Rejects_Invalid_Email(string email)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { email, password = "long-enough-password" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData("        ")]
    public async Task Registration_Rejects_Weak_Or_Blank_Password(string password)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { email = $"u{Guid.NewGuid():N}@example.com", password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_Rejects_Too_Long_Password_And_Unknown_Fields()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register", new { email, password = new string('p', 129) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "long-enough-password", isAdmin = true })).StatusCode);
    }

    [Fact]
    public async Task Token_Name_And_Completion_Response_Reject_Control_Characters()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var task = await CreateTaskAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/auth/tokens", Json("""{"name":"a\u0000b"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/tasks/{task.Id}/complete", Json("""{"response":"done\u0000"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/tasks/{task.Id}/complete", Json($$"""{"response":"{{new string('r', 4001)}}"}"""))).StatusCode);
        Assert.False((await client.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.IsCompleted);
    }

    [Fact]
    public async Task Email_Content_Is_Cleaned_Rather_Than_Rejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/tasks/from-email", Json(
            """{"subject":"Ofer\u0000tă\u0007 nouă","sender":"Ana\u0000 <ana@example.com>","senderEmail":"ana@example.com\u0000","messageId":"<id\u0000@host>","body":"Rând 1\u000c\nRând 2\u0000","receivedAt":"4501-01-01T00:00:00"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = (await response.Content.ReadApiAsync<TaskResponse>())!;
        Assert.Equal("Ofertă nouă", task.Title);
        Assert.Equal("De la: Ana <ana@example.com>\n\nRând 1\nRând 2", task.Description);
        Assert.Equal("ana@example.com", task.SourceEmailAddress);
    }

    [Theory]
    [InlineData("""{"subject":"x","dueDate":"2101-01-01"}""")]
    [InlineData("""{"subject":"x","attachment":"file"}""")]
    public async Task Email_Request_Is_Still_Validated_For_Shape(string body)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/tasks/from-email", Json(body))).StatusCode);
    }

    [Theory]
    [InlineData("/api/tasks/not-a-guid")]
    [InlineData("/api/tasks/123")]
    public async Task Malformed_Ids_Are_Not_Found(string url)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("plain text", false, true)]
    [InlineData("tab\there", false, false)]
    [InlineData("tab\there\nand a line", true, true)]
    [InlineData("nul\0", true, false)]
    [InlineData("emoji 😀", false, true)]
    public void Safe_Text_Check(string text, bool multiline, bool expected) =>
        Assert.Equal(expected, SafeText.IsSafe(text, multiline));

    // Built at run time: test discovery would replace unpaired surrogates in attribute data.
    [Fact]
    public void Unpaired_Surrogates_Are_Unsafe()
    {
        var high = ((char)0xD83D).ToString();
        var low = ((char)0xDC00).ToString();

        Assert.False(SafeText.IsSafe("lone high " + high, multiline: false));
        Assert.False(SafeText.IsSafe(low + " lone low", multiline: false));
        Assert.False(SafeText.IsSafe(low + high, multiline: false));
        Assert.True(SafeText.IsSafe(high + low, multiline: false));
    }

    [Fact]
    public void Clean_Removes_Only_What_Is_Unsafe()
    {
        var loneHigh = ((char)0xD83D).ToString();
        Assert.Equal("ab😀\nc", SafeText.Clean("a\0b😀\n" + loneHigh + "c\u0007", multiline: true));
    }

    [Fact]
    public async Task Half_A_Surrogate_Pair_Is_Not_Valid_Json()
    {
        // The JSON parser refuses it before validation runs; the Outlook macro avoids cutting text mid-pair.
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/tasks/from-email", Json("""{"subject":"x","body":"cut here \ud83d"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
