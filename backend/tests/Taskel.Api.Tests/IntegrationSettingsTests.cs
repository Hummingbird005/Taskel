using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Integrations;
using Taskel.Api.Keycloak;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>
/// The API with a fake Keycloak, on a database of its own: these tests change server-wide settings,
/// which would disturb the tests that share the main database.
/// </summary>
public class IntegrationsApiFactory : TaskelApiFactory
{
    public FakeKeycloak Keycloak { get; } = new();

    protected override string ConnectionString => WithDatabase("taskel_test_integrations");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:Smtp:Password", "configured-smtp-password");
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(KeycloakAdminClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Keycloak);
            services.Configure<OpenIdConnectOptions>(KeycloakOptions.Scheme, options => options.BackchannelHttpHandler = Keycloak);
        });
    }

    /// <summary>A signed-in administrator.</summary>
    public async Task<(HttpClient Client, UserResponse User)> CreateAdminAsync()
    {
        var (client, user) = await CreateUserAsync();
        await SetRoleAsync(user.Id, UserRole.Admin, null);
        return (client, user);
    }

    public async Task<T> InDbAsync<T>(Func<TaskelDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TaskelDbContext>());
    }
}

[CollectionDefinition(Name)]
public class IntegrationsCollection : ICollectionFixture<IntegrationsApiFactory>
{
    public const string Name = "Integrations";
}

[Collection(IntegrationsCollection.Name)]
public class IntegrationSettingsTests(IntegrationsApiFactory factory) : IAsyncLifetime
{
    private static readonly EmailSettingsRequest EmailForm = new(
        "smtp.saved.example", 2525, "taskel-sender", "saved-smtp-password", "tasks@saved.example", "Taskel Saved",
        RequireTls: true, NotificationsEnabled: true, PublicUrl: "https://taskel.saved.example/");

    private static readonly DirectorySettingsRequest DirectoryForm = new(
        Enabled: true, Host: "dc2.corp.example.com", Port: 636, Security: DirectorySecurity.Ldaps, Domain: "corp.example.com",
        BaseDn: "DC=corp,DC=example,DC=com", RequiredGroupDn: null, DepartmentAttribute: "department", ManagerGroupDn: null,
        AdminGroupDn: null, SyncGroups: true, GroupsBaseDn: null, TimeoutSeconds: 10);

    private static readonly KeycloakSettingsRequest KeycloakForm = new(
        Enabled: true, Authority: FakeKeycloak.Authority, ClientId: FakeKeycloak.ClientId, ClientSecret: FakeKeycloak.ClientSecret,
        DisplayName: "SSO Firmă", RequireHttpsMetadata: true, RequiredRole: null, AdminRole: "taskel-admin",
        ManagerRole: "taskel-manager", DepartmentClaim: "department", SyncGroups: true, GroupsClaim: "groups");

    // Every test starts and ends with the server configuration in effect.
    public Task InitializeAsync() => ResetAllAsync();

    public Task DisposeAsync() => ResetAllAsync();

    private async Task ResetAllAsync()
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IntegrationSettings>();
        foreach (var kind in IntegrationKinds.All) await settings.ResetAsync(kind);
    }

    // ---- Access ----

    [Theory]
    [InlineData("email")]
    [InlineData("directory")]
    [InlineData("keycloak")]
    public async Task Only_Administrators_Can_Read_The_Settings(string kind)
    {
        var (client, _) = await factory.CreateUserAsync();

        var response = await client.GetAsync($"/api/admin/integrations/{kind}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/admin/integrations/{kind}")).StatusCode);
    }

    [Fact]
    public async Task Only_Administrators_Can_Save_Settings()
    {
        var (client, user) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(user.Id, UserRole.Manager, "Vânzări");

        var response = await client.PutApiAsync("/api/admin/integrations/email", EmailForm);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Email ----

    [Fact]
    public async Task Email_Settings_Start_From_The_Server_Configuration()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var settings = await admin.GetApiAsync<EmailSettingsResponse>("/api/admin/integrations/email");

        Assert.Equal("smtp.test.invalid", settings!.Host);
        Assert.Equal("taskel@test.invalid", settings.FromAddress);
        Assert.False(settings.Status.Saved);
        Assert.True(settings.Status.SecretInConfiguration);
        Assert.False(settings.Status.SecretSaved);
        Assert.True(settings.Status.Ready);
    }

    [Fact]
    public async Task Saved_Email_Settings_Replace_The_Configuration_And_Never_Return_The_Password()
    {
        var (admin, me) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/email", EmailForm);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var saved = await response.Content.ReadApiAsync<EmailSettingsResponse>();

        Assert.DoesNotContain("saved-smtp-password", body);
        Assert.Equal("smtp.saved.example", saved!.Host);
        Assert.Equal(2525, saved.Port);
        Assert.Equal("https://taskel.saved.example", saved.PublicUrl);
        Assert.True(saved.Status.Saved);
        Assert.True(saved.Status.SecretSaved);
        Assert.Equal(me.DisplayName, saved.Status.UpdatedBy);

        // In use straight away, password included.
        var smtp = factory.Services.GetRequiredService<IntegrationSettings>().Smtp;
        Assert.Equal("smtp.saved.example", smtp.Host);
        Assert.Equal("saved-smtp-password", smtp.Password);

        // Stored encrypted, apart from the rest.
        var row = await factory.InDbAsync(db => db.IntegrationSettings.SingleAsync(s => s.Kind == IntegrationKinds.Email));
        Assert.DoesNotContain("saved-smtp-password", row.Settings);
        Assert.NotNull(row.ProtectedSecret);
        Assert.DoesNotContain("saved-smtp-password", row.ProtectedSecret);
    }

    [Fact]
    public async Task An_Empty_Password_Keeps_Or_Drops_The_Saved_One_As_Asked()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        var settings = factory.Services.GetRequiredService<IntegrationSettings>();
        (await admin.PutApiAsync("/api/admin/integrations/email", EmailForm)).EnsureSuccessStatusCode();

        // Left out: the saved password stays.
        (await admin.PutApiAsync("/api/admin/integrations/email", EmailForm with { Password = null, FromName = "Alt nume" }))
            .EnsureSuccessStatusCode();
        Assert.Equal("saved-smtp-password", settings.Smtp.Password);
        Assert.Equal("Alt nume", settings.Smtp.FromName);

        // Emptied: back to the configured password.
        var cleared = await (await admin.PutApiAsync("/api/admin/integrations/email", EmailForm with { Password = "" }))
            .Content.ReadApiAsync<EmailSettingsResponse>();
        Assert.False(cleared!.Status.SecretSaved);
        Assert.Equal("configured-smtp-password", settings.Smtp.Password);
    }

    [Fact]
    public async Task Resetting_Goes_Back_To_The_Server_Configuration()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        (await admin.PutApiAsync("/api/admin/integrations/email", EmailForm)).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync("/api/admin/integrations/email");

        var settings = await response.Content.ReadApiAsync<EmailSettingsResponse>();
        Assert.False(settings!.Status.Saved);
        Assert.Equal("smtp.test.invalid", settings.Host);
        Assert.Equal("configured-smtp-password", factory.Services.GetRequiredService<IntegrationSettings>().Smtp.Password);
    }

    [Fact]
    public async Task Turning_Notifications_Off_Applies_At_Once()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        var (client, _) = await factory.CreateUserAsync();

        (await admin.PutApiAsync("/api/admin/integrations/email", EmailForm with { NotificationsEnabled = false })).EnsureSuccessStatusCode();

        var mine = await client.GetApiAsync<NotificationSettingsResponse>("/api/notifications");
        Assert.False(mine!.Available);
    }

    [Theory]
    [InlineData("smtp.example.com:587", null, null)]
    [InlineData("smtp.example.com", "not-an-address", null)]
    [InlineData("smtp.example.com", null, "ftp://taskel.example.com")]
    [InlineData("smtp.example.com", null, "https://taskel.example.com/?x=1")]
    [InlineData("https://smtp.example.com", null, null)]
    public async Task Invalid_Email_Settings_Are_Refused(string host, string? from, string? publicUrl)
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/email",
            EmailForm with { Host = host, FromAddress = from ?? EmailForm.FromAddress, PublicUrl = publicUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_Settings_Without_Port_Or_Switches_Are_Refused()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutAsJsonAsync("/api/admin/integrations/email", new { host = "smtp.example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_Test_Message_Goes_To_The_Administrator_Through_The_Unsaved_Settings()
    {
        var (admin, me) = await factory.CreateAdminAsync();

        var response = await admin.PostApiAsync("/api/admin/integrations/email/test",
            new EmailTestRequest(EmailForm with { Host = "smtp.trial.example", Password = null }));

        response.EnsureSuccessStatusCode();
        var (message, smtp) = Assert.Single(factory.Emails.TestsTo(me.Email));
        Assert.Equal("smtp.trial.example", smtp.Host);
        // No password typed: the one in effect is used.
        Assert.Equal("configured-smtp-password", smtp.Password);
        Assert.Contains("test", message.Subject);
        // Nothing was saved.
        Assert.Equal("smtp.test.invalid", factory.Services.GetRequiredService<IntegrationSettings>().Smtp.Host);
    }

    [Fact]
    public async Task A_Failed_Test_Message_Reports_Why()
    {
        var (admin, me) = await factory.CreateAdminAsync();
        factory.Emails.FailFor(me.Email, () => new Taskel.Api.Email.EmailSendException("x", new InvalidOperationException("535 Authentication failed")));

        var response = await admin.PostApiAsync("/api/admin/integrations/email/test", new EmailTestRequest(EmailForm));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("535 Authentication failed", await response.Content.ReadAsStringAsync());
    }

    // ---- Active Directory ----

    [Fact]
    public async Task Switching_Active_Directory_Off_Removes_Domain_Sign_In()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        factory.Directory.AddAccount("off.test", "Domain-Pass-1");

        (await admin.PutApiAsync("/api/admin/integrations/directory", DirectoryForm with { Enabled = false })).EnsureSuccessStatusCode();

        var options = await factory.CreateClient().GetApiAsync<AuthOptionsResponse>("/api/auth/options");
        Assert.False(options!.ActiveDirectory);
        var login = await factory.CreateClient().PostApiAsync("/api/auth/login/directory", new DirectoryLoginRequest("off.test", "Domain-Pass-1"));
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
    }

    [Fact]
    public async Task Saved_Directory_Settings_Are_Used_For_Sign_In()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var saved = await (await admin.PutApiAsync("/api/admin/integrations/directory", DirectoryForm))
            .Content.ReadApiAsync<DirectorySettingsResponse>();

        Assert.Equal("dc2.corp.example.com", saved!.Host);
        Assert.True(saved.Status.Ready);
        Assert.Equal("dc2.corp.example.com", factory.Services.GetRequiredService<IntegrationSettings>().Directory.Host);
        var options = await factory.CreateClient().GetApiAsync<AuthOptionsResponse>("/api/auth/options");
        Assert.Equal("corp.example.com", options!.ActiveDirectoryDomain);
    }

    [Fact]
    public async Task Directory_Settings_That_Are_Switched_On_Need_A_Server_A_Domain_And_A_Base()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/directory", DirectoryForm with { Host = null, BaseDn = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Host", body);
        Assert.Contains("BaseDn", body);
    }

    [Fact]
    public async Task A_Department_Attribute_That_Could_Change_An_Ldap_Filter_Is_Refused()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/directory", DirectoryForm with { DepartmentAttribute = "department)(cn=*" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_Directory_Test_Signs_In_With_The_Form_Settings_And_Shows_The_Account()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        factory.Directory.AddAccount("test.ion", "Domain-Pass-1", displayName: "Ion Test", department: "IT", groups: "Suport");

        var response = await admin.PostApiAsync("/api/admin/integrations/directory/test",
            new DirectoryTestRequest(DirectoryForm with { Enabled = false, Host = "dc9.corp.example.com" }, "test.ion", "Domain-Pass-1"));

        var result = await response.Content.ReadApiAsync<DirectoryTestResponse>();
        Assert.True(result!.SignedIn);
        Assert.Equal("Ion Test", result.DisplayName);
        Assert.Equal("IT", result.Department);
        Assert.Equal(["Suport"], result.Groups);
        Assert.Equal("dc9.corp.example.com", factory.Directory.LastTestedWith!.Host);
        // A test creates no account.
        Assert.False(await factory.InDbAsync(db => db.Users.AnyAsync(u => u.Email == "test.ion@corp.example.com")));
    }

    [Fact]
    public async Task The_Directory_Test_Reports_A_Wrong_Password_And_An_Unreachable_Server()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        factory.Directory.AddAccount("test.ana", "Domain-Pass-1");

        var wrong = await (await admin.PostApiAsync("/api/admin/integrations/directory/test",
            new DirectoryTestRequest(DirectoryForm, "test.ana", "nope"))).Content.ReadApiAsync<DirectoryTestResponse>();
        Assert.False(wrong!.SignedIn);

        factory.Directory.Unavailable = true;
        try
        {
            var down = await admin.PostApiAsync("/api/admin/integrations/directory/test", new DirectoryTestRequest(DirectoryForm, "test.ana", "x"));
            Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
        }
        finally
        {
            factory.Directory.Unavailable = false;
        }
    }

    // ---- Keycloak settings ----

    [Fact]
    public async Task Keycloak_Is_Offered_On_The_Sign_In_Screen_Once_Saved()
    {
        var (admin, _) = await factory.CreateAdminAsync();
        var before = await factory.CreateClient().GetApiAsync<AuthOptionsResponse>("/api/auth/options");
        Assert.False(before!.Keycloak);

        var saved = await (await admin.PutApiAsync("/api/admin/integrations/keycloak", KeycloakForm))
            .Content.ReadApiAsync<KeycloakSettingsResponse>();

        Assert.True(saved!.Status.Ready);
        Assert.True(saved.Status.SecretSaved);
        Assert.Equal("https://taskel.test.invalid/api/auth/keycloak/callback", saved.RedirectUri);
        var after = await factory.CreateClient().GetApiAsync<AuthOptionsResponse>("/api/auth/options");
        Assert.True(after!.Keycloak);
        Assert.Equal("SSO Firmă", after.KeycloakName);
    }

    [Theory]
    [InlineData("http://keycloak.test/realms/taskel", true)]
    [InlineData("https://keycloak.test/auth", true)]
    [InlineData("https://keycloak.test/realms/taskel?x=1", true)]
    [InlineData(null, true)]
    public async Task Invalid_Keycloak_Settings_Are_Refused(string? authority, bool requireHttps)
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/keycloak",
            KeycloakForm with { Authority = authority, RequireHttpsMetadata = requireHttps });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plain_Http_Is_Accepted_Only_When_Https_Is_Explicitly_Not_Required()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var response = await admin.PutApiAsync("/api/admin/integrations/keycloak",
            KeycloakForm with { Authority = "http://localhost:8080/realms/taskel", RequireHttpsMetadata = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_Keycloak_Test_Checks_The_Realm_And_The_Service_Account()
    {
        var (admin, _) = await factory.CreateAdminAsync();

        var check = await (await admin.PostApiAsync("/api/admin/integrations/keycloak/test", new KeycloakTestRequest(KeycloakForm)))
            .Content.ReadApiAsync<KeycloakCheck>();

        Assert.Equal(FakeKeycloak.Authority, check!.Issuer);
        Assert.True(check.ServiceAccount);
        Assert.True(check.CanListUsers);

        var wrongSecret = await (await admin.PostApiAsync("/api/admin/integrations/keycloak/test",
            new KeycloakTestRequest(KeycloakForm with { ClientSecret = "wrong" }))).Content.ReadApiAsync<KeycloakCheck>();
        Assert.False(wrongSecret!.ServiceAccount);
        Assert.NotNull(wrongSecret.Problem);

        var wrongRealm = await admin.PostApiAsync("/api/admin/integrations/keycloak/test",
            new KeycloakTestRequest(KeycloakForm with { Authority = "https://keycloak.test/realms/other" }));
        Assert.Equal(HttpStatusCode.BadGateway, wrongRealm.StatusCode);
    }

    // ---- Keycloak sign-in ----

    private async Task<HttpClient> SaveKeycloakAsync(KeycloakSettingsRequest? form = null)
    {
        var (admin, _) = await factory.CreateAdminAsync();
        (await admin.PutApiAsync("/api/admin/integrations/keycloak", form ?? KeycloakForm)).EnsureSuccessStatusCode();
        return admin;
    }

    /// <summary>Goes through the whole sign-in as a browser would, with Keycloak saying <paramref name="idClaims"/>.</summary>
    private async Task<(HttpClient Browser, HttpResponseMessage Callback)> SignInWithKeycloakAsync(
        Dictionary<string, object> idClaims, Dictionary<string, object>? accessClaims = null)
    {
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var start = await browser.GetAsync("/api/auth/keycloak/login");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorize = start.Headers.Location!;
        Assert.StartsWith(FakeKeycloak.Authority + "/protocol/openid-connect/auth", authorize.AbsoluteUri);
        var query = System.Web.HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal(FakeKeycloak.ClientId, query["client_id"]);
        Assert.Equal("https://taskel.test.invalid/api/auth/keycloak/callback", query["redirect_uri"]);
        // The code comes back in the query: the default for the code flow, so the parameter may be left out.
        Assert.Contains(query["response_mode"], new[] { null, "query" });
        Assert.Equal("S256", query["code_challenge_method"]);

        var code = factory.Keycloak.Authorize(query["nonce"]!, idClaims, accessClaims);
        var callback = await browser.GetAsync(
            $"/api/auth/keycloak/callback?code={code}&state={Uri.EscapeDataString(query["state"]!)}");
        return (browser, callback);
    }

    private static Dictionary<string, object> Person(string subject, string email, bool verified = true, params string[] groups) => new()
    {
        ["sub"] = subject,
        ["email"] = email,
        ["email_verified"] = verified,
        ["name"] = "Ioana Keycloak",
        ["department"] = "Ofertare",
        ["groups"] = groups
    };

    private static readonly string[] AdminRoleOnly = ["taskel-admin"];

    private static Dictionary<string, object> Roles(params string[] roles) => new()
    {
        ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
    };

    [Fact]
    public async Task A_Keycloak_Sign_In_Creates_The_Account_With_Its_Role_Department_And_Groups()
    {
        await SaveKeycloakAsync();
        var subject = Guid.NewGuid().ToString();
        var email = $"kc-{subject[..8]}@example.com";

        var (browser, callback) = await SignInWithKeycloakAsync(Person(subject, email, groups: ["/Firma/Ofertare KC"]), Roles("taskel-manager"));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("https://taskel.test.invalid/", callback.Headers.Location!.AbsoluteUri);
        var me = await browser.GetApiAsync<UserResponse>("/api/auth/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Ioana Keycloak", me.DisplayName);
        Assert.Equal("Ofertare", me.Department);
        Assert.Equal(UserRole.Manager, me.Role);
        Assert.True(me.IsKeycloakAccount);
        var group = await factory.InDbAsync(db => db.Groups.SingleAsync(g => g.DirectoryDn == "keycloak:/firma/ofertare kc"));
        Assert.Equal("Ofertare KC", group.Name);
        Assert.Equal(GroupKind.Keycloak, group.Kind);
    }

    [Fact]
    public async Task The_Admin_Role_In_Keycloak_Grants_And_Withdraws_Administration()
    {
        await SaveKeycloakAsync();
        var subject = Guid.NewGuid().ToString();
        var email = $"kc-{subject[..8]}@example.com";

        var (first, _) = await SignInWithKeycloakAsync(Person(subject, email), Roles("taskel-admin"));
        Assert.Equal(UserRole.Admin, (await first.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);

        var (second, _) = await SignInWithKeycloakAsync(Person(subject, email), Roles());
        Assert.Equal(UserRole.User, (await second.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);
    }

    [Fact]
    public async Task Client_Roles_Count_As_Well_As_Realm_Roles()
    {
        await SaveKeycloakAsync();
        var subject = Guid.NewGuid().ToString();

        var (browser, _) = await SignInWithKeycloakAsync(Person(subject, $"kc-{subject[..8]}@example.com"), new Dictionary<string, object>
        {
            ["resource_access"] = new Dictionary<string, object>
            {
                [FakeKeycloak.ClientId] = new Dictionary<string, object> { ["roles"] = AdminRoleOnly }
            }
        });

        Assert.Equal(UserRole.Admin, (await browser.GetApiAsync<UserResponse>("/api/auth/me"))!.Role);
    }

    [Fact]
    public async Task Without_The_Required_Role_Sign_In_Is_Refused()
    {
        await SaveKeycloakAsync(KeycloakForm with { RequiredRole = "taskel-user" });
        var subject = Guid.NewGuid().ToString();

        var (browser, callback) = await SignInWithKeycloakAsync(Person(subject, $"kc-{subject[..8]}@example.com"), Roles("other"));

        Assert.Equal("https://taskel.test.invalid/?loginError=keycloak-role", callback.Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_Local_Account_Is_Taken_Over_Only_On_A_Verified_Address()
    {
        await SaveKeycloakAsync();
        var (_, local) = await factory.CreateUserAsync();

        var (_, unverified) = await SignInWithKeycloakAsync(Person(Guid.NewGuid().ToString(), local.Email, verified: false));
        Assert.Equal("https://taskel.test.invalid/?loginError=keycloak-email-unverified", unverified.Headers.Location!.AbsoluteUri);

        var (browser, _) = await SignInWithKeycloakAsync(Person(Guid.NewGuid().ToString(), local.Email));
        var me = await browser.GetApiAsync<UserResponse>("/api/auth/me");
        Assert.Equal(local.Id, me!.Id);
        Assert.True(me.IsKeycloakAccount);

        // The local password no longer works.
        var login = await factory.CreateClient().PostApiAsync("/api/auth/login", new LoginRequest(local.Email, "test-password-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task A_Directory_Account_Is_Never_Taken_Over()
    {
        await SaveKeycloakAsync();
        factory.Directory.AddAccount("kc.dir", "Domain-Pass-1", email: "kc.dir@corp.example.com");
        (await factory.CreateClient().PostApiAsync("/api/auth/login/directory", new DirectoryLoginRequest("kc.dir", "Domain-Pass-1")))
            .EnsureSuccessStatusCode();

        var (_, callback) = await SignInWithKeycloakAsync(Person(Guid.NewGuid().ToString(), "kc.dir@corp.example.com"));

        Assert.Equal("https://taskel.test.invalid/?loginError=keycloak-email-taken", callback.Headers.Location!.AbsoluteUri);
    }

    [Fact]
    public async Task A_Keycloak_Account_Is_Never_Taken_Over_By_The_Directory()
    {
        await SaveKeycloakAsync();
        var (_, _) = await SignInWithKeycloakAsync(Person(Guid.NewGuid().ToString(), "kc.first@corp.example.com"));
        factory.Directory.AddAccount("kc.first", "Domain-Pass-1", email: "kc.first@corp.example.com");

        var login = await factory.CreateClient().PostApiAsync("/api/auth/login/directory", new DirectoryLoginRequest("kc.first", "Domain-Pass-1"));

        Assert.Equal(HttpStatusCode.Conflict, login.StatusCode);
    }

    [Fact]
    public async Task A_Changed_Callback_State_Is_Refused()
    {
        await SaveKeycloakAsync();
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await browser.GetAsync("/api/auth/keycloak/login");

        var callback = await browser.GetAsync("/api/auth/keycloak/callback?code=x&state=forged");

        Assert.Equal("https://taskel.test.invalid/?loginError=keycloak", callback.Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Keycloak_Sign_In_Is_Not_Found_While_Keycloak_Is_Not_Set_Up()
    {
        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/auth/keycloak/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signing_Out_Of_A_Keycloak_Session_Gives_The_Keycloak_Sign_Out_Address()
    {
        await SaveKeycloakAsync();
        var subject = Guid.NewGuid().ToString();
        var (browser, _) = await SignInWithKeycloakAsync(Person(subject, $"kc-{subject[..8]}@example.com"));

        var response = await browser.PostAsync("/api/auth/logout", null);

        var logout = await response.Content.ReadApiAsync<LogoutResponse>();
        Assert.StartsWith(FakeKeycloak.Authority + "/protocol/openid-connect/logout?", logout!.LogoutUrl);
        Assert.Contains("id_token_hint=", logout.LogoutUrl);
        Assert.Contains("post_logout_redirect_uri=https%3A%2F%2Ftaskel.test.invalid%2F", logout.LogoutUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_Out_Of_A_Local_Session_Gives_No_Address()
    {
        await SaveKeycloakAsync();
        var (client, _) = await factory.CreateUserAsync();

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- Keycloak user import ----

    [Fact]
    public async Task Importing_Creates_Updates_And_Skips_Accounts()
    {
        var admin = await SaveKeycloakAsync();
        var (_, local) = await factory.CreateUserAsync();
        var existingSubject = Guid.NewGuid().ToString();
        await SignInWithKeycloakAsync(Person(existingSubject, $"kc-{existingSubject[..8]}@example.com"));
        factory.Keycloak.Users.Clear();
        factory.Keycloak.Users.AddRange(
        [
            new { id = Guid.NewGuid().ToString(), username = "nou", email = "Nou.Import@Example.com", emailVerified = false,
                firstName = "Nou", lastName = "Importat", enabled = true,
                attributes = new Dictionary<string, string[]> { ["department"] = ["Logistică"] } },
            new { id = existingSubject, username = "existent", email = $"kc-{existingSubject[..8]}@example.com", emailVerified = true,
                firstName = "Nume", lastName = "Schimbat", enabled = true },
            new { id = Guid.NewGuid().ToString(), username = "local", email = local.Email, emailVerified = false, enabled = true },
            new { id = Guid.NewGuid().ToString(), username = "fara-email", enabled = true },
            new { id = Guid.NewGuid().ToString(), username = "oprit", email = "oprit@example.com", enabled = false }
        ]);

        var result = await (await admin.PostAsync("/api/admin/integrations/keycloak/import", null))
            .Content.ReadApiAsync<KeycloakImportResponse>();

        Assert.Equal(4, result!.Total);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Skipped, s => s.Reason == KeycloakRefusal.EmailUnverified && s.Who == local.Email);
        Assert.Contains(result.Skipped, s => s.Reason == KeycloakRefusal.NoEmail);
        var created = await factory.InDbAsync(db => db.Users.SingleAsync(u => u.Email == "nou.import@example.com"));
        Assert.Equal("Nou Importat", created.DisplayName);
        Assert.Equal("Logistică", created.Department);
        Assert.Equal(string.Empty, created.PasswordHash);
        var updated = await factory.InDbAsync(db => db.Users.SingleAsync(u => u.KeycloakSubject == existingSubject));
        Assert.Equal("Nume Schimbat", updated.DisplayName);
    }

    [Fact]
    public async Task Importing_Reports_A_Refused_Service_Account()
    {
        var admin = await SaveKeycloakAsync();
        factory.Keycloak.CanListUsers = false;
        try
        {
            var response = await admin.PostAsync("/api/admin/integrations/keycloak/import", null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("view-users", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            factory.Keycloak.CanListUsers = true;
        }
    }

    [Fact]
    public async Task Saving_New_Keycloak_Settings_Applies_To_The_Next_Sign_In()
    {
        await SaveKeycloakAsync();
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var first = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync("/api/auth/keycloak/login")).Headers.Location!.Query);
        Assert.Equal(FakeKeycloak.ClientId, first["client_id"]);

        await SaveKeycloakAsync(KeycloakForm with { ClientId = "taskel-renamed" });

        var second = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync("/api/auth/keycloak/login")).Headers.Location!.Query);
        Assert.Equal("taskel-renamed", second["client_id"]);
    }
}
