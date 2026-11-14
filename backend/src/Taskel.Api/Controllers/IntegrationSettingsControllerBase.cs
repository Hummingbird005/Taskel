using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Integrations;

namespace Taskel.Api.Controllers;

/// <summary>
/// What the administrator's integration settings pages share: the route, the access rule and the
/// status every answer carries. Secrets (the SMTP password, the Keycloak client secret) are
/// accepted but never sent back.
/// </summary>
[ApiController]
[Authorize]
[AdminOnly("Only administrators can change the integrations.")]
public abstract class IntegrationSettingsControllerBase(TaskelDbContext db, IntegrationSettings settings) : ControllerBase
{
    protected const string RoutePrefix = "api/admin/integrations/";

    protected IntegrationSettings Settings => settings;
    protected TaskelDbContext Db => db;

    protected async Task<IntegrationStatus> StatusAsync(string kind, bool ready, CancellationToken ct)
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
    protected static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        return inner.Message;
    }
}
