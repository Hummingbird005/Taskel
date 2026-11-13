using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Email;
using Taskel.Api.Integrations;
using Taskel.Api.Keycloak;

namespace Taskel.Api.Controllers;

/// <summary>
/// The administrator's settings for the mail server, Active Directory and Keycloak. Secrets
/// (the SMTP password, the Keycloak client secret) are accepted but never sent back.
/// </summary>
[ApiController]
[Authorize]
[AdminOnly("Only administrators can change the integrations.")]
[Route("api/admin/integrations")]
public class IntegrationsAdminController(
    TaskelDbContext db,
    IntegrationSettings settings,
    IEmailSender email,
    IDirectoryAuthenticator directory,
    KeycloakAdminClient keycloak,
    KeycloakAccounts keycloakAccounts,
    ILogger<IntegrationsAdminController> logger) : ControllerBase
{
    private const int MaxSkippedListed = 50;

    // ---- Email ----

    [HttpGet("email")]
    public async Task<ActionResult<EmailSettingsResponse>> GetEmail(CancellationToken ct) => Ok(await EmailResponseAsync(ct));

    [HttpPut("email")]
    public async Task<ActionResult<EmailSettingsResponse>> SaveEmail(EmailSettingsRequest request, CancellationToken ct)
    {
        await settings.SaveEmailAsync(request.ToSettings(), request.Password, User.GetUserId(), ct);
        return Ok(await EmailResponseAsync(ct));
    }

    [HttpDelete("email")]
    public async Task<ActionResult<EmailSettingsResponse>> ResetEmail(CancellationToken ct)
    {
        await settings.ResetAsync(IntegrationKinds.Email, ct);
        return Ok(await EmailResponseAsync(ct));
    }

    /// <summary>Sends a message with the settings in the form (not yet saved) to the administrator's own address.</summary>
    [HttpPost("email/test")]
    [EnableRateLimiting(AuthController.LoginRateLimit)]
    public async Task<ActionResult<EmailTestResponse>> TestEmail(EmailTestRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var to = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (to is null) return Unauthorized();

        var candidate = request.Settings.ToSettings().Smtp;
        candidate.Password = settings.ResolveSecret(IntegrationKinds.Email, request.Settings.Password);
        if (!candidate.IsConfigured)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter the SMTP server and the sender address.");

        try
        {
            await email.SendTestAsync(new EmailMessage(to, "Taskel: mesaj de test",
                "Acesta este un mesaj de test trimis din Taskel, din pagina Integrări a administratorului.\n\n" +
                "Dacă l-ai primit, setările serverului de email funcționează."), candidate, ct);
        }
        catch (EmailSendException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The SMTP server did not accept the message.",
                detail: Reason(ex));
        }
        return Ok(new EmailTestResponse(to));
    }

    // ---- Active Directory ----

    [HttpGet("directory")]
    public async Task<ActionResult<DirectorySettingsResponse>> GetDirectory(CancellationToken ct) =>
        Ok(await DirectoryResponseAsync(ct));

    [HttpPut("directory")]
    public async Task<ActionResult<DirectorySettingsResponse>> SaveDirectory(DirectorySettingsRequest request, CancellationToken ct)
    {
        await settings.SaveDirectoryAsync(request.ToOptions(), User.GetUserId(), ct);
        return Ok(await DirectoryResponseAsync(ct));
    }

    [HttpDelete("directory")]
    public async Task<ActionResult<DirectorySettingsResponse>> ResetDirectory(CancellationToken ct)
    {
        await settings.ResetAsync(IntegrationKinds.Directory, ct);
        return Ok(await DirectoryResponseAsync(ct));
    }

    /// <summary>Signs in to the directory with the settings in the form, and shows what Taskel would read. Changes nothing.</summary>
    [HttpPost("directory/test")]
    [EnableRateLimiting(AuthController.LoginRateLimit)]
    public async Task<ActionResult<DirectoryTestResponse>> TestDirectory(DirectoryTestRequest request, CancellationToken ct)
    {
        var options = request.Settings.ToOptions();
        // A test is about the connection; it runs whether or not sign-in is switched on yet.
        options.Enabled = true;
        if (!options.IsUsable)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter the domain controller, the domain and where to look for users.");

        DirectoryUser? found;
        try
        {
            found = await directory.AuthenticateAsync(options, request.Username, request.Password, ct);
        }
        catch (DirectoryUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message, detail: Reason(ex));
        }

        return Ok(found is null
            ? new DirectoryTestResponse(false, null, null, null, null, null, [])
            : new DirectoryTestResponse(true, found.Email, found.DisplayName, found.Department, found.IsManager, found.IsAdmin,
                found.Groups?.Select(g => g.Name).ToList() ?? []));
    }

    // ---- Keycloak ----

    [HttpGet("keycloak")]
    public async Task<ActionResult<KeycloakSettingsResponse>> GetKeycloak(CancellationToken ct) => Ok(await KeycloakResponseAsync(ct));

    [HttpPut("keycloak")]
    public async Task<ActionResult<KeycloakSettingsResponse>> SaveKeycloak(KeycloakSettingsRequest request, CancellationToken ct)
    {
        await settings.SaveKeycloakAsync(request.ToOptions(), request.ClientSecret, User.GetUserId(), ct);
        return Ok(await KeycloakResponseAsync(ct));
    }

    [HttpDelete("keycloak")]
    public async Task<ActionResult<KeycloakSettingsResponse>> ResetKeycloak(CancellationToken ct)
    {
        await settings.ResetAsync(IntegrationKinds.Keycloak, ct);
        return Ok(await KeycloakResponseAsync(ct));
    }

    /// <summary>Reads the realm's discovery document and tries the client secret, with the settings in the form.</summary>
    [HttpPost("keycloak/test")]
    public async Task<ActionResult<KeycloakCheck>> TestKeycloak(KeycloakTestRequest request, CancellationToken ct)
    {
        var options = request.Settings.ToOptions();
        options.ClientSecret = settings.ResolveSecret(IntegrationKinds.Keycloak, request.Settings.ClientSecret);
        if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.ClientId))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter the realm address and the client id.");

        try
        {
            return Ok(await keycloak.CheckAsync(options, ct));
        }
        catch (KeycloakUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    /// <summary>
    /// Creates or updates a Taskel account for every enabled user of the realm, with the saved
    /// settings, so tasks can be given to people before they first sign in.
    /// </summary>
    [HttpPost("keycloak/import")]
    public async Task<ActionResult<KeycloakImportResponse>> ImportKeycloakUsers(CancellationToken ct)
    {
        var options = settings.KeycloakCopy();
        if (!options.IsUsable)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Save and switch on the Keycloak settings first.");

        IReadOnlyList<KeycloakUser> users;
        try
        {
            users = await keycloak.ListUsersAsync(options, ct);
        }
        catch (KeycloakUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }

        int created = 0, updated = 0;
        var skipped = new List<KeycloakImportSkip>();
        foreach (var user in users)
        {
            var link = await keycloakAccounts.ImportAsync(user, ct);
            if (link.User is null) skipped.Add(new KeycloakImportSkip(user.Email ?? user.DisplayName ?? user.Subject, link.Refusal!.Value));
            else if (link.Created) created++;
            else updated++;
        }
        logger.LogInformation("Keycloak import: {Created} created, {Updated} updated, {Skipped} skipped", created, updated, skipped.Count);
        return Ok(new KeycloakImportResponse(users.Count, created, updated, skipped.Count, skipped.Take(MaxSkippedListed).ToList()));
    }

    // ---- Responses ----

    private async Task<EmailSettingsResponse> EmailResponseAsync(CancellationToken ct)
    {
        var smtp = settings.Smtp;
        var notifications = settings.Notifications;
        return new EmailSettingsResponse(smtp.Host, smtp.Port, smtp.Username, smtp.FromAddress, smtp.FromName, smtp.RequireTls,
            notifications.Enabled, notifications.PublicUrl, await StatusAsync(IntegrationKinds.Email, smtp.IsConfigured, ct));
    }

    private async Task<DirectorySettingsResponse> DirectoryResponseAsync(CancellationToken ct)
    {
        var d = settings.Directory;
        return new DirectorySettingsResponse(d.Enabled, d.Host, d.Port, d.Security, d.Domain, d.BaseDn, d.RequiredGroupDn,
            d.DepartmentAttribute, d.ManagerGroupDn, d.AdminGroupDn, d.SyncGroups, d.GroupsBaseDn, d.TimeoutSeconds,
            await StatusAsync(IntegrationKinds.Directory, d.IsUsable, ct));
    }

    private async Task<KeycloakSettingsResponse> KeycloakResponseAsync(CancellationToken ct)
    {
        var k = settings.Keycloak;
        return new KeycloakSettingsResponse(k.Enabled, k.Authority, k.ClientId, k.DisplayName, k.RequireHttpsMetadata,
            k.RequiredRole, k.AdminRole, k.ManagerRole, k.DepartmentClaim, k.SyncGroups, k.GroupsClaim,
            KeycloakOidcEvents.RedirectUri(Request, settings), PublicAddress.Of(Request, settings) + "/",
            await StatusAsync(IntegrationKinds.Keycloak, k.IsUsable, ct));
    }

    private async Task<IntegrationStatus> StatusAsync(string kind, bool ready, CancellationToken ct)
    {
        var state = settings.StateOf(kind);
        string? updatedBy = null;
        if (state.UpdatedById is { } id)
        {
            updatedBy = await db.Users.AsNoTracking().Where(u => u.Id == id)
                .Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct);
        }
        return new IntegrationStatus(state.Saved, state.UpdatedAt, updatedBy, state.SecretSaved, state.SecretInConfiguration,
            state.SecretUnreadable, ready);
    }

    /// <summary>The innermost error message, which usually names the actual problem (refused login, certificate, timeout).</summary>
    private static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        return inner.Message;
    }
}
