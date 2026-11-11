using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthApiTests(TaskelApiFactory factory)
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_SignsIn_And_Me_Returns_User()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var registered = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "correct-horse"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Me_WithoutSession_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_IsCaseInsensitive_Conflict()
    {
        var email = NewEmail();
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "correct-horse"));

        var second = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), "another-pass"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "long-enough-password")]
    [InlineData("valid@example.com", "short")]
    public async Task Register_InvalidInput_ReturnsBadRequest(string email, string password)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Works_With_Correct_Password_Only()
    {
        var email = NewEmail();
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "correct-horse"));

        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(NewEmail(), "correct-horse"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "correct-horse"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_Ends_Session()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    [Fact]
    public async Task Password_Is_Stored_Hashed()
    {
        var email = NewEmail();
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "correct-horse"));

        var hash = await factory.GetPasswordHashAsync(email);
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain("correct-horse", hash);
    }
}
