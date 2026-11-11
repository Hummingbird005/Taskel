using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskel.Api.Data;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Taskel", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>A client with its own cookie jar, signed in as a freshly registered user.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@example.com", "test-password-123"));
        response.EnsureSuccessStatusCode();
        return client;
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

    public async Task<string?> GetPasswordHashAsync(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.PasswordHash).SingleOrDefaultAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}

/// <summary>Shares one API instance and one database across all API test classes.</summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<TaskelApiFactory>
{
    public const string Name = "Api";
}
