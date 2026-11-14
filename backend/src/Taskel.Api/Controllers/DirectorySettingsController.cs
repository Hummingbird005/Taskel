using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Integrations;

namespace Taskel.Api.Controllers;

/// <summary>Sign-in with Active Directory accounts over LDAP.</summary>
[Route(RoutePrefix + "directory")]
public class DirectorySettingsController(
    TaskelDbContext db,
    IntegrationSettings settings,
    IDirectoryAuthenticator directory) : IntegrationSettingsControllerBase(db, settings)
{
    [HttpGet]
    public async Task<ActionResult<DirectorySettingsResponse>> GetDirectory(CancellationToken ct) =>
        Ok(await DirectoryResponseAsync(ct));

    [HttpPut]
    public async Task<ActionResult<DirectorySettingsResponse>> SaveDirectory(DirectorySettingsRequest request, CancellationToken ct)
    {
        await Settings.SaveDirectoryAsync(request.ToOptions(), User.GetUserId(), ct);
        return Ok(await DirectoryResponseAsync(ct));
    }

    [HttpDelete]
    public async Task<ActionResult<DirectorySettingsResponse>> ResetDirectory(CancellationToken ct)
    {
        await Settings.ResetAsync(IntegrationKinds.Directory, ct);
        return Ok(await DirectoryResponseAsync(ct));
    }

    /// <summary>Signs in to the directory with the settings in the form, and shows what Taskel would read. Changes nothing.</summary>
    [HttpPost("test")]
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

    private async Task<DirectorySettingsResponse> DirectoryResponseAsync(CancellationToken ct)
    {
        var d = Settings.Directory;
        return new DirectorySettingsResponse(d.Enabled, d.Host, d.Port, d.Security, d.Domain, d.BaseDn, d.RequiredGroupDn,
            d.DepartmentAttribute, d.ManagerGroupDn, d.AdminGroupDn, d.SyncGroups, d.GroupsBaseDn, d.TimeoutSeconds,
            await StatusAsync(IntegrationKinds.Directory, d.IsUsable, ct));
    }
}
