using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Taskel.Api.Directory;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class DirectoryLoginTests(TaskelApiFactory factory)
{
    private static string NewName() => $"u{Guid.NewGuid():N}"[..16];

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login/directory", new DirectoryLoginRequest(username, password));

    [Fact]
    public async Task Options_Report_What_The_Sign_In_Screen_Should_Offer()
    {
        var options = await factory.CreateClient().GetFromJsonAsync<AuthOptionsResponse>("/api/auth/options");

        Assert.True(options!.ActiveDirectory);
        Assert.Equal("corp.example.com", options.ActiveDirectoryDomain);
        Assert.True(options.LocalRegistration);
    }

    [Fact]
    public async Task First_Sign_In_Creates_The_Account_From_The_Directory()
    {
        var name = NewName();
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1", displayName: "Ion Popescu");
        var client = factory.CreateClient();

        var response = await SignInAsync(client, name, "Domain-Pass-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadApiAsync<UserResponse>())!;
        Assert.Equal(account.Email, user.Email);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal("Ion Popescu", user.DisplayName);
        Assert.True(user.IsDirectoryAccount);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);

        var listed = (await client.GetFromJsonAsync<List<UserSummary>>("/api/users"))!;
        Assert.Contains(listed, u => u.Id == user.Id && u.DisplayName == "Ion Popescu");
    }

    [Fact]
    public async Task Signing_In_Again_Reuses_The_Account_And_Follows_Directory_Changes()
    {
        var name = NewName();
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1", displayName: "Old Name");
        var first = (await (await SignInAsync(factory.CreateClient(), name, "Domain-Pass-1")).Content.ReadApiAsync<UserResponse>())!;

        var newEmail = $"renamed-{Guid.NewGuid():N}@corp.example.com";
        factory.Directory.Replace(name, "Domain-Pass-1", account with { Email = newEmail, DisplayName = "New Name" });
        var second = (await (await SignInAsync(factory.CreateClient(), name, "Domain-Pass-1")).Content.ReadApiAsync<UserResponse>())!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(newEmail, second.Email);
        Assert.Equal("New Name", second.DisplayName);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData(" Domain-Pass-1")]
    public async Task Wrong_Password_Is_Rejected(string password)
    {
        var name = NewName();
        factory.Directory.AddAccount(name, "Domain-Pass-1");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, name, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_User_And_Invalid_Input_Are_Rejected()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, NewName(), "whatever-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync(client, "", "whatever-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync(client, "user", "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync(client, new string('u', 129), "whatever-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/auth/login/directory",
                new StringContent("""{"username":"a\u0000b","password":"x"}""", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Directory_Outage_Is_Reported_As_Unavailable()
    {
        var name = NewName();
        factory.Directory.AddAccount(name, "Domain-Pass-1");
        factory.Directory.Unavailable = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SignInAsync(factory.CreateClient(), name, "Domain-Pass-1")).StatusCode);
        }
        finally
        {
            factory.Directory.Unavailable = false;
        }
    }

    [Fact]
    public async Task Directory_Account_Cannot_Sign_In_With_A_Local_Password()
    {
        var name = NewName();
        var account = factory.Directory.AddAccount(name, "Domain-Pass-1");
        await SignInAsync(factory.CreateClient(), name, "Domain-Pass-1");

        var client = factory.CreateClient();
        foreach (var password in new[] { "Domain-Pass-1", "", " " })
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password });
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Local_Account_With_The_Same_Email_Is_Taken_Over_And_Keeps_Its_Tasks()
    {
        var (local, localUser) = await factory.CreateUserAsync();
        await local.PostApiAsync("/api/tasks", new CreateTaskRequest("Created before AD", null));
        var name = NewName();
        factory.Directory.AddAccount(name, "Domain-Pass-1", email: localUser.Email);

        var viaDirectory = factory.CreateClient();
        var signedIn = (await (await SignInAsync(viaDirectory, name, "Domain-Pass-1")).Content.ReadApiAsync<UserResponse>())!;

        Assert.Equal(localUser.Id, signedIn.Id);
        Assert.Single((await viaDirectory.GetApiAsync<List<TaskResponse>>("/api/tasks"))!);
        // The old local password no longer opens the account.
        var oldPassword = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(localUser.Email, "test-password-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Fact]
    public async Task Two_Directory_Accounts_Cannot_Share_An_Email()
    {
        var email = $"shared-{Guid.NewGuid():N}@corp.example.com";
        var first = NewName();
        var second = NewName();
        factory.Directory.AddAccount(first, "Domain-Pass-1", email: email);
        factory.Directory.AddAccount(second, "Domain-Pass-2", email: email);

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(factory.CreateClient(), first, "Domain-Pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SignInAsync(factory.CreateClient(), second, "Domain-Pass-2")).StatusCode);
    }

    [Fact]
    public async Task Sign_In_Attempts_Are_Rate_Limited()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:LoginAttemptsPerMinute", "3"));
        var client = limited.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++) statuses.Add((await SignInAsync(client, "nobody", "wrong-pass")).StatusCode);
        statuses.Add((await SignInAsync(client, "nobody", "wrong-pass")).StatusCode);
        statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("a@example.com", "wrong-pass"))).StatusCode);

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
             HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
    }

    [Fact]
    public async Task Directory_Sign_In_Is_Off_Unless_Configured_And_Local_Registration_Can_Be_Turned_Off()
    {
        using var localOnly = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ActiveDirectory:Enabled", "false")
            .UseSetting("Auth:AllowLocalRegistration", "false"));
        var client = localOnly.CreateClient();

        var options = await client.GetFromJsonAsync<AuthOptionsResponse>("/api/auth/options");
        Assert.False(options!.ActiveDirectory);
        Assert.Null(options.ActiveDirectoryDomain);
        Assert.False(options.LocalRegistration);

        Assert.Equal(HttpStatusCode.NotFound, (await SignInAsync(client, "user", "Domain-Pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest($"u{Guid.NewGuid():N}@example.com", "long-enough-password"))).StatusCode);
    }

    [Theory]
    [InlineData("ion", "ion", "ion@corp.example.com")]
    [InlineData("  ion.popescu  ", "ion.popescu", "ion.popescu@corp.example.com")]
    [InlineData(@"CORP\ion", "ion", "ion@corp.example.com")]
    [InlineData("ion@other.example.org", "ion", "ion@other.example.org")]
    public void User_Names_Are_Normalised(string input, string account, string upn)
    {
        Assert.True(DirectoryNames.TryParse(input, "corp.example.com", out var login));
        Assert.Equal(new DirectoryLogin(account, upn), login);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ion)(objectClass=*")]
    [InlineData("ion*")]
    [InlineData("ion popescu")]
    [InlineData(@"CORP\ion@corp.example.com")]
    [InlineData(@"bad domain\ion")]
    [InlineData("ion@")]
    [InlineData("ion@bad_domain")]
    [InlineData("ion@a..b")]
    [InlineData("ion\0")]
    [InlineData("a,DC=corp")]
    public void Suspicious_User_Names_Are_Refused(string input) =>
        Assert.False(DirectoryNames.TryParse(input, "corp.example.com", out _));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("(x)", @"\28x\29")]
    [InlineData(@"a\b", @"a\5cb")]
    [InlineData("a\0b", @"a\00b")]
    public void Filter_Values_Are_Escaped(string value, string expected) =>
        Assert.Equal(expected, DirectoryNames.EscapeFilterValue(value));

    [Fact]
    public async Task Real_Authenticator_Refuses_Empty_Passwords_And_Bad_Names_Without_Contacting_The_Directory()
    {
        // Host is unroutable on purpose: reaching it would time out and throw instead of returning null.
        var authenticator = new LdapDirectoryAuthenticator(
            Microsoft.Extensions.Options.Options.Create(new DirectoryOptions
            {
                Enabled = true, Host = "192.0.2.1", Domain = "corp.example.com", BaseDn = "DC=corp,DC=example,DC=com", TimeoutSeconds = 1
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LdapDirectoryAuthenticator>.Instance);

        Assert.Null(await authenticator.AuthenticateAsync("ion", ""));
        Assert.Null(await authenticator.AuthenticateAsync("ion)(uid=*", "Domain-Pass-1"));
    }

    [Fact]
    public async Task Real_Authenticator_Reports_An_Unreachable_Or_Unconfigured_Directory()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<LdapDirectoryAuthenticator>.Instance;
        var unconfigured = new LdapDirectoryAuthenticator(Microsoft.Extensions.Options.Options.Create(new DirectoryOptions()), logger);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => unconfigured.AuthenticateAsync("ion", "Domain-Pass-1"));

        // Port 1 on localhost: nothing listens there, so the connection is refused at once.
        var unreachable = new LdapDirectoryAuthenticator(
            Microsoft.Extensions.Options.Options.Create(new DirectoryOptions
            {
                Enabled = true, Host = "127.0.0.1", Port = 1, Security = DirectorySecurity.None,
                Domain = "corp.example.com", BaseDn = "DC=corp,DC=example,DC=com", TimeoutSeconds = 2
            }), logger);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => unreachable.AuthenticateAsync("ion", "Domain-Pass-1"));
    }
}
