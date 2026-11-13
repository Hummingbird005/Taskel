using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// The sample data, seeded into a database of its own so it does not mix with the other tests.
/// </summary>
public class DemoDataTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        (Environment.GetEnvironmentVariable("TASKEL_TEST_CONNECTION")
         ?? "Host=localhost;Port=5432;Database=taskel_test;Username=taskel;Password=taskel")
        .Replace("Database=taskel_test", "Database=taskel_test_demo", StringComparison.OrdinalIgnoreCase);

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Taskel", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");
    });

    public async Task InitializeAsync()
    {
        Assert.Contains("taskel_test_demo", ConnectionString);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TaskelDbContext>().Database.EnsureDeletedAsync();
        await _factory.DisposeAsync();
    }

    private async Task<bool> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await DemoData.SeedAsync(
            scope.ServiceProvider.GetRequiredService<TaskelDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
            DateOnly.FromDateTime(DateTime.Now));
    }

    private async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task Every_Demo_Account_Signs_In_With_Its_Published_Password_And_Role()
    {
        Assert.True(await SeedAsync());

        Assert.Contains(DemoData.Accounts, a => a.Role == UserRole.Admin);
        Assert.Contains(DemoData.Accounts, a => a.Role == UserRole.Manager);
        Assert.Contains(DemoData.Accounts, a => a.Role == UserRole.User);
        foreach (var account in DemoData.Accounts)
        {
            var client = await SignInAsync(account.Email, account.Password);
            var me = (await client.GetApiAsync<UserResponse>("/api/auth/me"))!;
            Assert.Equal(account.Role, me.Role);
            Assert.Equal(account.Department, me.Department);
            Assert.Equal(account.DisplayName, me.DisplayName);
            Assert.False(me.IsDirectoryAccount);
        }
    }

    [Fact]
    public async Task The_Sample_Tasks_Cover_Every_List_In_The_Interface()
    {
        Assert.True(await SeedAsync());

        var ion = await SignInAsync("manager.vanzari@taskel.example", DemoData.ManagerPassword);
        var ionTasks = (await ion.GetApiAsync<List<TaskResponse>>("/api/tasks"))!;
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Contains(ionTasks, t => t is { Access: TaskAccess.Creator, Assignee: null, Group: null, IsCompleted: false } && t.DueDate < today);
        Assert.Contains(ionTasks, t => t is { Assignee: null, Group: null, IsCompleted: false } && t.DueDate == today);
        Assert.Contains(ionTasks, t => t.StartDate is not null && t.DueDate > t.StartDate);
        Assert.Contains(ionTasks, t => t.IsCompleted);
        Assert.Contains(ionTasks, t => t.SourceEmailAddress is not null);
        Assert.Contains(ionTasks, t => t is { Access: TaskAccess.Creator, Assignee: not null });
        Assert.Contains(ionTasks, t => t is { Access: TaskAccess.Creator, Group: not null, Assignee: null });

        // Waiting on a group, for its members; forwarded, for the person who passed it on.
        var maria = await SignInAsync("maria.ionescu@taskel.example", DemoData.UserPassword);
        var mariaTasks = (await maria.GetApiAsync<List<TaskResponse>>("/api/tasks"))!;
        Assert.Contains(mariaTasks, t => t.Access == TaskAccess.Group);
        var forwarded = Assert.Single(mariaTasks, t => t.Access == TaskAccess.Viewer);
        Assert.Equal("Andrei Stan", forwarded.Assignee!.DisplayName);
        Assert.Equal("Maria Ionescu", Assert.Single(forwarded.Forwards).From.DisplayName);

        // Managers see their own department, and the administrator every account.
        var team = (await ion.GetApiAsync<TeamResponse>("/api/team"))!;
        Assert.Equal("Vânzări", team.Department);
        Assert.Equal(4, team.Members.Count);
        var radu = await SignInAsync("manager.suport@taskel.example", DemoData.ManagerPassword);
        Assert.Equal(2, (await radu.GetApiAsync<TeamResponse>("/api/team"))!.Members.Count);
        var admin = await SignInAsync("admin@taskel.example", DemoData.AdminPassword);
        Assert.Equal(DemoData.Accounts.Count, (await admin.GetApiAsync<List<AdminUserResponse>>("/api/admin/users"))!.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await maria.GetAsync("/api/team")).StatusCode);
    }

    [Fact]
    public async Task Seeding_Leaves_A_Database_That_Already_Has_Accounts_Alone()
    {
        Assert.True(await SeedAsync());
        Assert.False(await SeedAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        Assert.Equal(DemoData.Accounts.Count, await db.Users.CountAsync());
        Assert.Equal(1, await db.Groups.CountAsync());
    }

    [Fact]
    public async Task Demo_Passwords_Are_Stored_Hashed()
    {
        Assert.True(await SeedAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
        foreach (var user in await db.Users.ToListAsync())
        {
            Assert.NotEmpty(user.PasswordHash);
            Assert.DoesNotContain("Demo", user.PasswordHash);
        }
    }
}
