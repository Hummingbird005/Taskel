using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Controllers;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class ApiTokenTests(TaskelApiFactory factory)
{
    [Fact]
    public async Task Token_Authenticates_As_Its_Owner()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var (tokenClient, _) = await factory.CreateTokenClientAsync(session);

        var created = await tokenClient.PostApiAsync("/api/tasks", new CreateTaskRequest("Via token", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seenBySession = (await session.GetApiAsync<List<TaskItem>>("/api/tasks"))!;
        Assert.Contains(seenBySession, t => t.Title == "Via token");
    }

    [Fact]
    public async Task Token_Is_Shown_Once_And_Stored_Only_As_A_Hash()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var (_, created) = await factory.CreateTokenClientAsync(session, "My laptop");

        Assert.StartsWith("tsk_", created.Token);
        Assert.True(created.Token.Length >= 40);

        var listJson = await session.GetStringAsync("/api/auth/tokens");
        Assert.Contains("My laptop", listJson);
        Assert.DoesNotContain(created.Token, listJson);
        Assert.DoesNotContain("hash", listJson, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(created.Token, await factory.GetTokenHashesAsync());
    }

    [Theory]
    [InlineData("tsk_this-token-does-not-exist")]
    [InlineData("garbage")]
    public async Task Unknown_Token_Is_Rejected(string token)
    {
        var response = await factory.CreateBearerClient(token).GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_Token_Is_Rejected_Even_With_A_Valid_Session_Cookie()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        request.Headers.Authorization = new("Bearer", "tsk_wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Revoked_Token_Stops_Working()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var (tokenClient, token) = await factory.CreateTokenClientAsync(session);
        Assert.Equal(HttpStatusCode.OK, (await tokenClient.GetAsync("/api/tasks")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/auth/tokens/{token.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenClient.GetAsync("/api/tasks")).StatusCode);
        Assert.Empty((await session.GetFromJsonAsync<List<ApiTokenResponse>>("/api/auth/tokens"))!);
    }

    [Fact]
    public async Task Token_Cannot_Manage_Tokens()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var (tokenClient, token) = await factory.CreateTokenClientAsync(session);

        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenClient.GetAsync("/api/auth/tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await tokenClient.PostAsJsonAsync("/api/auth/tokens", new CreateApiTokenRequest("More"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await tokenClient.DeleteAsync($"/api/auth/tokens/{token.Id}")).StatusCode);
    }

    [Fact]
    public async Task Users_Cannot_See_Or_Revoke_Each_Others_Tokens()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var (aliceTokenClient, aliceToken) = await factory.CreateTokenClientAsync(alice);

        Assert.Empty((await bob.GetFromJsonAsync<List<ApiTokenResponse>>("/api/auth/tokens"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/auth/tokens/{aliceToken.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await aliceTokenClient.GetAsync("/api/tasks")).StatusCode);
    }

    [Fact]
    public async Task Token_Management_Requires_A_Session()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/tokens")).StatusCode);
    }

    [Fact]
    public async Task Last_Used_Is_Recorded()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        var (tokenClient, _) = await factory.CreateTokenClientAsync(session);
        Assert.Null((await session.GetFromJsonAsync<List<ApiTokenResponse>>("/api/auth/tokens"))!.Single().LastUsedAt);

        await tokenClient.GetAsync("/api/tasks");

        Assert.NotNull((await session.GetFromJsonAsync<List<ApiTokenResponse>>("/api/auth/tokens"))!.Single().LastUsedAt);
    }

    [Fact]
    public async Task Token_Count_Is_Limited_And_Name_Is_Required()
    {
        var session = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await session.PostAsJsonAsync("/api/auth/tokens", new CreateApiTokenRequest(""))).StatusCode);

        for (var i = 0; i < ApiTokensController.MaxTokensPerUser; i++)
            await factory.CreateTokenClientAsync(session, $"Token {i}");

        var oneTooMany = await session.PostAsJsonAsync("/api/auth/tokens", new CreateApiTokenRequest("Extra"));
        Assert.Equal(HttpStatusCode.BadRequest, oneTooMany.StatusCode);
    }
}
