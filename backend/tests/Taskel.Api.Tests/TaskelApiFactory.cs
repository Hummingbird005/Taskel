using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// Runs the API against a real PostgreSQL test database. Override the connection string with
/// the TASKEL_TEST_CONNECTION environment variable (used by CI).
/// </summary>
public class TaskelApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("TASKEL_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=taskel_test;Username=taskel;Password=taskel";

    /// <summary>Stands in for the SMTP server; holds the replies the API tried to send.</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Where uploaded files land during the tests: a folder of its own, removed afterwards.</summary>
    public string AttachmentsPath { get; } = Path.Combine(Path.GetTempPath(), $"taskel-tests-{Guid.NewGuid():N}");

    /// <summary>Stands in for Active Directory.</summary>
    public FakeDirectory Directory { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Taskel", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ActiveDirectory:Enabled", "true");
        builder.UseSetting("ActiveDirectory:Host", "dc.corp.example.com");
        builder.UseSetting("ActiveDirectory:Domain", "corp.example.com");
        builder.UseSetting("ActiveDirectory:BaseDn", "DC=corp,DC=example,DC=com");
        // The whole suite signs in from one address; the limit has its own test.
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
        builder.UseSetting("Attachments:Path", AttachmentsPath);
        builder.UseSetting("Attachments:MaxFilesPerTask", "3");
        // Enough for Taskel to consider email set up; the fake sender takes the messages.
        builder.UseSetting("Email:Smtp:Host", "smtp.test.invalid");
        builder.UseSetting("Email:Smtp:FromAddress", "taskel@test.invalid");
        builder.UseSetting("Email:Notifications:PublicUrl", "https://taskel.test.invalid");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<IDirectoryAuthenticator>();
            services.AddSingleton<IDirectoryAuthenticator>(Directory);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>A client with its own cookie jar, signed in as a freshly registered user.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync() => (await CreateUserAsync()).Client;

    /// <summary>Registers a new user and returns their signed-in client and account.</summary>
    public async Task<(HttpClient Client, UserResponse User)> CreateUserAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@example.com", "test-password-123"));
        response.EnsureSuccessStatusCode();
        return (client, (await response.Content.ReadApiAsync<UserResponse>())!);
    }

    /// <summary>Creates an API token through <paramref name="sessionClient"/> and returns a client that sends it.</summary>
    public async Task<(HttpClient Client, CreatedApiTokenResponse Token)> CreateTokenClientAsync(
        HttpClient sessionClient, string name = "Outlook")
    {
        var response = await sessionClient.PostAsJsonAsync("/api/auth/tokens", new CreateApiTokenRequest(name));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<CreatedApiTokenResponse>())!;
        return (CreateBearerClient(token.Token), token);
    }

    /// <summary>A client without cookies that sends <paramref name="token"/> as a bearer token.</summary>
    public HttpClient CreateBearerClient(string token)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public async Task<List<string>> GetTokenHashesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        return await db.ApiTokens.Select(t => t.TokenHash).ToListAsync();
    }

    /// <summary>Sets a user's role and department directly, to arrange a test.</summary>
    public async Task SetRoleAsync(Guid userId, UserRole role, string? department)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.Role = role;
        user.Department = department;
        await db.SaveChangesAsync();
    }

    /// <summary>Moves a task's completion time into the past, to arrange a test.</summary>
    public async Task SetCompletedAtAsync(Guid taskId, DateTime completedAt)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
        task.CompletedAt = completedAt;
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetPasswordHashAsync(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.PasswordHash).SingleOrDefaultAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            if (System.IO.Directory.Exists(AttachmentsPath)) System.IO.Directory.Delete(AttachmentsPath, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing the run for.
        }
        return Task.CompletedTask;
    }
}

/// <summary>Shares one API instance and one database across all API test classes.</summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<TaskelApiFactory>
{
    public const string Name = "Api";
}
