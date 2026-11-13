using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Email;
using Taskel.Api.Keycloak;

namespace Taskel.Api.Integrations;

/// <summary>
/// The settings of one integration as an administrator saved them. The secret (a password or a
/// client secret) is kept apart from the rest, encrypted with ASP.NET Core Data Protection.
/// </summary>
public class IntegrationSetting
{
    public string Kind { get; set; } = string.Empty;
    /// <summary>The settings as JSON, without the secret.</summary>
    public string Settings { get; set; } = "{}";
    /// <summary>The secret, encrypted. Null when none was saved: the server configuration's is used.</summary>
    public string? ProtectedSecret { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedById { get; set; }
}

/// <summary>The integrations an administrator can set up in the interface.</summary>
public static class IntegrationKinds
{
    public const string Email = "email";
    public const string Directory = "directory";
    public const string Keycloak = "keycloak";

    public static readonly IReadOnlyList<string> All = [Email, Directory, Keycloak];
}

/// <summary>Sending email: the SMTP server, and the notifications sent through it.</summary>
public sealed class EmailSettings
{
    public SmtpOptions Smtp { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
}

/// <summary>Where an integration's settings come from right now.</summary>
/// <param name="Saved">True when an administrator saved them; false when they come from the server configuration.</param>
/// <param name="SecretSaved">A secret was saved in the interface.</param>
/// <param name="SecretInConfiguration">The server configuration has a secret, used when none is saved.</param>
/// <param name="SecretUnreadable">A secret was saved but can no longer be decrypted (the keys were lost).</param>
public sealed record IntegrationState(
    bool Saved,
    DateTime? UpdatedAt,
    Guid? UpdatedById,
    bool SecretSaved,
    bool SecretInConfiguration,
    bool SecretUnreadable)
{
    public static readonly IntegrationState Unsaved = new(false, null, null, false, false, false);
}

/// <summary>
/// The settings Taskel works with for email, Active Directory and Keycloak. The server configuration
/// (appsettings, environment variables) gives the starting values; settings an administrator saves
/// replace them, all at once for that integration. A secret left empty in the interface falls back
/// to the configuration's, so it can stay in an environment variable.
/// </summary>
/// <remarks>
/// Kept in memory and read without waiting, because the sign-in handlers need them synchronously.
/// They are reloaded after every change made here and every minute (<see cref="IntegrationSettingsRefresher"/>),
/// so several servers sharing a database converge within a minute.
/// </remarks>
public sealed class IntegrationSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtector _protector;
    private readonly IOptionsMonitorCache<OpenIdConnectOptions> _oidcOptions;
    private readonly ILogger<IntegrationSettings> _logger;
    private readonly EmailSettings _configuredEmail;
    private readonly DirectoryOptions _configuredDirectory;
    private readonly KeycloakOptions _configuredKeycloak;
    private readonly SemaphoreSlim _reloading = new(1, 1);
    private volatile Snapshot _current;

    private sealed record Snapshot(
        EmailSettings Email,
        DirectoryOptions Directory,
        KeycloakOptions Keycloak,
        IReadOnlyDictionary<string, IntegrationState> States);

    public IntegrationSettings(
        IServiceScopeFactory scopes,
        IDataProtectionProvider dataProtection,
        IOptionsMonitorCache<OpenIdConnectOptions> oidcOptions,
        IOptions<SmtpOptions> smtp,
        IOptions<NotificationOptions> notifications,
        IOptions<DirectoryOptions> directory,
        IOptions<KeycloakOptions> keycloak,
        ILogger<IntegrationSettings> logger)
    {
        _scopes = scopes;
        _protector = dataProtection.CreateProtector("Taskel.IntegrationSecrets.v1");
        _oidcOptions = oidcOptions;
        _logger = logger;
        _configuredEmail = new EmailSettings { Smtp = Clone(smtp.Value), Notifications = Clone(notifications.Value) };
        _configuredDirectory = Clone(directory.Value);
        _configuredKeycloak = Clone(keycloak.Value);
        _current = FromConfiguration();
    }

    public SmtpOptions Smtp => _current.Email.Smtp;
    public NotificationOptions Notifications => _current.Email.Notifications;
    public DirectoryOptions Directory => _current.Directory;
    public KeycloakOptions Keycloak => _current.Keycloak;

    /// <summary>Copies of the current settings, safe to change (e.g. to try new values before saving them).</summary>
    public EmailSettings EmailCopy() => Clone(_current.Email);
    public DirectoryOptions DirectoryCopy() => Clone(_current.Directory);
    public KeycloakOptions KeycloakCopy() => Clone(_current.Keycloak);

    public IntegrationState StateOf(string kind) =>
        _current.States.TryGetValue(kind, out var state) ? state : IntegrationState.Unsaved;

    /// <summary>The configured secret for an integration, used when none is saved in the interface.</summary>
    public string? ConfiguredSecret(string kind) => kind switch
    {
        IntegrationKinds.Email => NullIfEmpty(_configuredEmail.Smtp.Password),
        IntegrationKinds.Keycloak => NullIfEmpty(_configuredKeycloak.ClientSecret),
        _ => null
    };

    /// <summary>The secret in effect now: the one saved in the interface, otherwise the configured one.</summary>
    public string? CurrentSecret(string kind) => kind switch
    {
        IntegrationKinds.Email => NullIfEmpty(Smtp.Password),
        IntegrationKinds.Keycloak => NullIfEmpty(Keycloak.ClientSecret),
        _ => null
    };

    /// <summary>
    /// What a secret field in a form means: null keeps the secret in effect now, an empty string
    /// drops the saved one (back to the configured secret), anything else replaces it.
    /// </summary>
    public string? ResolveSecret(string kind, string? submitted) => submitted switch
    {
        null => CurrentSecret(kind),
        "" => ConfiguredSecret(kind),
        _ => submitted
    };

    /// <summary>Reads the saved settings again. Keeps the current ones, and returns false, when the database cannot be read.</summary>
    public async Task<bool> ReloadAsync(CancellationToken ct = default)
    {
        await _reloading.WaitAsync(ct);
        try
        {
            List<IntegrationSetting> rows;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
                rows = await db.IntegrationSettings.AsNoTracking().ToListAsync(ct);
            }

            var next = Build(rows);
            var keycloakChanged = !SameJson(next.Keycloak, _current.Keycloak);
            _current = next;
            // The sign-in handler keeps its options; drop them so the next request builds them from the new settings.
            if (keycloakChanged) _oidcOptions.TryRemove(KeycloakOptions.Scheme);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "The integration settings could not be read from the database; the current ones are kept");
            return false;
        }
        finally
        {
            _reloading.Release();
        }
    }

    public Task SaveEmailAsync(EmailSettings settings, string? password, Guid by, CancellationToken ct = default)
    {
        var copy = Clone(settings);
        copy.Smtp.Password = null;
        return SaveAsync(IntegrationKinds.Email, Serialize(copy), password, by, ct);
    }

    public Task SaveDirectoryAsync(DirectoryOptions settings, Guid by, CancellationToken ct = default) =>
        SaveAsync(IntegrationKinds.Directory, Serialize(settings), secret: null, by, ct);

    public Task SaveKeycloakAsync(KeycloakOptions settings, string? clientSecret, Guid by, CancellationToken ct = default)
    {
        var copy = Clone(settings);
        copy.ClientSecret = null;
        return SaveAsync(IntegrationKinds.Keycloak, Serialize(copy), clientSecret, by, ct);
    }

    /// <summary>Forgets what was saved for an integration, so the server configuration applies again.</summary>
    public async Task ResetAsync(string kind, CancellationToken ct = default)
    {
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
            await db.IntegrationSettings.Where(s => s.Kind == kind).ExecuteDeleteAsync(ct);
        }
        await ReloadAsync(ct);
    }

    /// <param name="secret">Null keeps the saved secret, empty removes it, anything else replaces it.</param>
    private async Task SaveAsync(string kind, string json, string? secret, Guid by, CancellationToken ct)
    {
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskelDbContext>();
            var row = await db.IntegrationSettings.FirstOrDefaultAsync(s => s.Kind == kind, ct);
            if (row is null)
            {
                row = new IntegrationSetting { Kind = kind };
                db.IntegrationSettings.Add(row);
            }
            row.Settings = json;
            if (secret is not null) row.ProtectedSecret = secret.Length == 0 ? null : _protector.Protect(secret);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedById = by;
            await db.SaveChangesAsync(ct);
        }
        await ReloadAsync(ct);
    }

    private Snapshot FromConfiguration() => new(
        Clone(_configuredEmail), Clone(_configuredDirectory), Clone(_configuredKeycloak),
        IntegrationKinds.All.ToDictionary(kind => kind, kind => IntegrationState.Unsaved with
        {
            SecretInConfiguration = ConfiguredSecret(kind) is not null
        }));

    private Snapshot Build(IReadOnlyCollection<IntegrationSetting> rows)
    {
        var email = Clone(_configuredEmail);
        var directory = Clone(_configuredDirectory);
        var keycloak = Clone(_configuredKeycloak);
        var states = new Dictionary<string, IntegrationState>();

        foreach (var kind in IntegrationKinds.All)
        {
            var row = rows.FirstOrDefault(r => r.Kind == kind);
            var configuredSecret = ConfiguredSecret(kind);
            if (row is null)
            {
                states[kind] = IntegrationState.Unsaved with { SecretInConfiguration = configuredSecret is not null };
                continue;
            }

            var (secret, unreadable) = Unprotect(kind, row.ProtectedSecret);
            var effectiveSecret = secret ?? configuredSecret;
            try
            {
                switch (kind)
                {
                    case IntegrationKinds.Email:
                        email = Deserialize<EmailSettings>(row.Settings);
                        email.Smtp.Password = effectiveSecret;
                        break;
                    case IntegrationKinds.Directory:
                        directory = Deserialize<DirectoryOptions>(row.Settings);
                        break;
                    case IntegrationKinds.Keycloak:
                        keycloak = Deserialize<KeycloakOptions>(row.Settings);
                        keycloak.ClientSecret = effectiveSecret;
                        break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "The saved {Kind} settings cannot be read; the server configuration is used instead", kind);
                states[kind] = IntegrationState.Unsaved with { SecretInConfiguration = configuredSecret is not null };
                continue;
            }

            states[kind] = new IntegrationState(
                true, row.UpdatedAt, row.UpdatedById, row.ProtectedSecret is not null, configuredSecret is not null, unreadable);
        }

        return new Snapshot(email, directory, keycloak, states);
    }

    private (string? Secret, bool Unreadable) Unprotect(string kind, string? protectedSecret)
    {
        if (protectedSecret is null) return (null, false);
        try
        {
            return (_protector.Unprotect(protectedSecret), false);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "The saved {Kind} secret cannot be decrypted: the Data Protection keys changed. Enter it again in the interface", kind);
            return (null, true);
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T Deserialize<T>(string json) where T : new() =>
        JsonSerializer.Deserialize<T>(json, Json) ?? new T();

    private static T Clone<T>(T value) where T : new() => Deserialize<T>(Serialize(value));

    private static bool SameJson<T>(T a, T b) => Serialize(a) == Serialize(b);

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Reloads the saved integration settings every minute, so changes made on another server arrive here too.</summary>
public sealed class IntegrationSettingsRefresher(IntegrationSettings settings) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await settings.ReloadAsync(stoppingToken);
        }
    }
}
