using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Data;
using Taskel.Api.Integrations;
using Taskel.Api.Keycloak;

namespace Taskel.Api.Controllers;

/// <summary>Sign-in with Keycloak, and the import of its users.</summary>
[Route(RoutePrefix + "keycloak")]
public class KeycloakSettingsController(
    TaskelDbContext db,
    IntegrationSettings settings,
    KeycloakAdminClient keycloak,
    KeycloakAccounts keycloakAccounts,
    ILogger<KeycloakSettingsController> logger) : IntegrationSettingsControllerBase(db, settings)
{
    private const int MaxSkippedListed = 50;

    [HttpGet]
    public async Task<ActionResult<KeycloakSettingsResponse>> GetKeycloak(CancellationToken ct) => Ok(await KeycloakResponseAsync(ct));

    [HttpPut]
    public async Task<ActionResult<KeycloakSettingsResponse>> SaveKeycloak(KeycloakSettingsRequest request, CancellationToken ct)
    {
        await Settings.SaveKeycloakAsync(request.ToOptions(), request.ClientSecret, User.GetUserId(), ct);
        return Ok(await KeycloakResponseAsync(ct));
    }

    [HttpDelete]
    public async Task<ActionResult<KeycloakSettingsResponse>> ResetKeycloak(CancellationToken ct)
    {
        await Settings.ResetAsync(IntegrationKinds.Keycloak, ct);
        return Ok(await KeycloakResponseAsync(ct));
    }

    /// <summary>Reads the realm's discovery document and tries the client secret, with the settings in the form.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<KeycloakCheck>> TestKeycloak(KeycloakTestRequest request, CancellationToken ct)
    {
        var options = request.Settings.ToOptions();
        options.ClientSecret = Settings.ResolveSecret(IntegrationKinds.Keycloak, request.Settings.ClientSecret);
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
    [HttpPost("import")]
    public async Task<ActionResult<KeycloakImportResponse>> ImportKeycloakUsers(CancellationToken ct)
    {
        var options = Settings.KeycloakCopy();
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
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Keycloak import: {Created} created, {Updated} updated, {Skipped} skipped", created, updated, skipped.Count);
        return Ok(new KeycloakImportResponse(users.Count, created, updated, skipped.Count, skipped.Take(MaxSkippedListed).ToList()));
    }

    private async Task<KeycloakSettingsResponse> KeycloakResponseAsync(CancellationToken ct)
    {
        var k = Settings.Keycloak;
        return new KeycloakSettingsResponse(k.Enabled, k.Authority, k.ClientId, k.DisplayName, k.RequireHttpsMetadata,
            k.RequiredRole, k.AdminRole, k.ManagerRole, k.DepartmentClaim, k.SyncGroups, k.GroupsClaim,
            KeycloakOidcEvents.RedirectUri(Request, Settings), PublicAddress.HomeOf(Request, Settings),
            await StatusAsync(IntegrationKinds.Keycloak, k.IsUsable, ct));
    }
}
