using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class DashboardTests(TaskelApiFactory factory)
{
    private static async Task<DashboardSettings> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardSettings>("/api/dashboard"))!;

    [Fact]
    public async Task New_User_Has_The_Default_Layout_And_The_Reminder_On()
    {
        var settings = await GetAsync(await factory.CreateAuthenticatedClientAsync());

        Assert.Null(settings.Widgets);
        Assert.True(settings.ShowDueAlert);
    }

    [Fact]
    public async Task Layout_Is_Saved_In_Order()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var saved = await client.PutAsJsonAsync("/api/dashboard",
            new DashboardSettings(["overdue", "stats", "tags"], ShowDueAlert: false));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var settings = await GetAsync(client);
        Assert.Equal(["overdue", "stats", "tags"], settings.Widgets);
        Assert.False(settings.ShowDueAlert);
    }

    [Fact]
    public async Task Empty_Layout_Is_Kept_And_Null_Restores_The_Default()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync("/api/dashboard", new DashboardSettings([]));
        Assert.Empty((await GetAsync(client)).Widgets!);

        await client.PutAsJsonAsync("/api/dashboard", new DashboardSettings(null));
        Assert.Null((await GetAsync(client)).Widgets);
    }

    [Fact]
    public async Task Each_User_Has_Their_Own_Dashboard()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();

        await alice.PutAsJsonAsync("/api/dashboard", new DashboardSettings(["today"], ShowDueAlert: false));

        var bobs = await GetAsync(bob);
        Assert.Null(bobs.Widgets);
        Assert.True(bobs.ShowDueAlert);
    }

    public static TheoryData<object> InvalidSettings => new()
    {
        new { widgets = new[] { "stats", "stats" } },
        new { widgets = new[] { "Not Valid" } },
        new { widgets = new[] { "" } },
        new { widgets = new[] { new string('a', 41) } },
        new { widgets = new string?[] { null } },
        new { widgets = Enumerable.Range(0, WidgetIdsAttribute.MaxWidgets + 1).Select(i => $"w{i}").ToArray() },
        new { widgets = "stats" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task Invalid_Layout_Is_Rejected_And_Nothing_Changes(object body)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/dashboard", new DashboardSettings(["stats"]));

        var result = await client.PutAsJsonAsync("/api/dashboard", body);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(["stats"], (await GetAsync(client)).Widgets);
    }

    [Fact]
    public async Task Requires_Authentication()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/dashboard", new DashboardSettings(null))).StatusCode);
    }
}
