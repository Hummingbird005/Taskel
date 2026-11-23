using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Attachments;
using Taskel.Api.Auth;
using System.Threading.RateLimiting;
using Taskel.Api.Controllers;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Email;
using Taskel.Api.Integrations;
using Taskel.Api.Keycloak;
using Taskel.Api.Models;
using Taskel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];

// The API takes small JSON documents; refuse anything larger outright.
// File uploads raise this limit for their own request, up to the attachment size limit.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1_000_000);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Enums travel as camelCase strings ("low", "medium", "high"); numbers are rejected.
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    // A property the API does not know is a mistake in the request, not something to ignore.
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddOpenApi();
var connectionString = builder.Configuration.GetConnectionString("Taskel")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Taskel' is not configured.");
builder.Services.AddDbContext<TaskelDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ITaskRepository, EfTaskRepository>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.Section));
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddScoped<TaskNotifier>();
builder.Services.AddScoped<Distributions>();
builder.Services.AddScoped<PriorityLevels>();
builder.Services.Configure<DirectoryOptions>(builder.Configuration.GetSection(DirectoryOptions.Section));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.AddSingleton<IDirectoryAuthenticator, LdapDirectoryAuthenticator>();
builder.Services.AddScoped<DirectoryAccounts>();
builder.Services.AddScoped<ExternalAccounts>();

// Settings an administrator can change in the interface (mail server, Active Directory, Keycloak).
// The configuration above gives their starting values.
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.Section));
builder.Services.AddSingleton<IntegrationDefaults>();
builder.Services.AddSingleton<IntegrationSettings>();
builder.Services.AddHostedService<IntegrationSettingsRefresher>();
builder.Services.AddScoped<KeycloakAccounts>();
builder.Services.AddScoped<KeycloakOidcEvents>();
builder.Services.AddScoped<KeycloakLogout>();
builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, KeycloakOidcSetup>();
builder.Services.AddHttpClient<KeycloakAdminClient>(KeycloakAdminClient.HttpClientName, client =>
    client.Timeout = TimeSpan.FromSeconds(15));

// Keys for the session cookies and for the secrets saved in the interface. Kept in the database by
// default, so they survive restarts and are shared by every server; DataProtection:KeysPath keeps
// them in a folder instead (away from the database backups that hold the encrypted secrets).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Taskel");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath)) dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
else dataProtection.PersistKeysToDbContext<TaskelDbContext>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.Configure<AttachmentOptions>(builder.Configuration.GetSection(AttachmentOptions.Section));
builder.Services.AddScoped<AttachmentStore>();

// Sign-in attempts are limited per client address, to slow password guessing and to keep
// repeated failures from locking accounts in Active Directory.
var loginAttemptsPerMinute = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>()?.LoginAttemptsPerMinute
                             ?? new AuthOptions().LoginAttemptsPerMinute;
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthController.LoginRateLimit, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, loginAttemptsPerMinute),
                Window = TimeSpan.FromMinutes(1)
            }));
});
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// Browsers use cookie sessions: an HttpOnly cookie, so no token is exposed to JavaScript.
// Other clients (e.g. the Outlook macro) send a personal API token as "Authorization: Bearer tsk_…".
const string CookieOrApiTokenScheme = "CookieOrApiToken";
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services
    .AddAuthentication(CookieOrApiTokenScheme)
    .AddPolicyScheme(CookieOrApiTokenScheme, displayName: null, options =>
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? ApiTokens.Scheme
                : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokens.Scheme, configureOptions: null)
    // Set up from the current Keycloak settings by KeycloakOidcSetup.
    .AddOpenIdConnect(KeycloakOptions.Scheme, displayName: "Keycloak", _ => { })
    .AddCookie(options =>
    {
        options.Cookie.Name = "taskel.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // This is an API: answer with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply pending migrations on startup when enabled (on by default in Development).
if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TaskelDbContext>().Database.MigrateAsync();
}

// The integration settings saved in the interface. If the database cannot be read yet, the
// configuration applies until the next reload.
await app.Services.GetRequiredService<IntegrationSettings>().ReloadAsync();

// Sample accounts and tasks, for trying Taskel out. Off unless asked for (on in Development):
// the passwords of these accounts are published in docs/DEMO-ACCOUNTS.md.
if (app.Configuration.GetValue("Seed:DemoData", false) && app.Environment.IsProduction())
{
    // The demo passwords are public. A production server never gets these accounts, whatever the setting says.
    app.Logger.LogError("Seed:DemoData is set on a Production server. The demo accounts were NOT added: their passwords are public.");
}
else if (app.Configuration.GetValue("Seed:DemoData", false))
{
    using var scope = app.Services.CreateScope();
    var seeded = await DemoData.SeedAsync(
        scope.ServiceProvider.GetRequiredService<TaskelDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        DateOnly.FromDateTime(DateTime.Now));
    if (seeded)
    {
        app.Logger.LogWarning(
            "Demo accounts and tasks were added (Seed:DemoData). Their passwords are public: see docs/DEMO-ACCOUNTS.md. " +
            "Never enable this on a server real people use.");
    }
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

