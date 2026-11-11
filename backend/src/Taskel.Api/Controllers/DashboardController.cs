using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>The signed-in user's dashboard preferences.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DashboardController(TaskelDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardSettings>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? Unauthorized() : Ok(new DashboardSettings(user.DashboardWidgets, user.ShowDueAlert));
    }

    [HttpPut]
    public async Task<ActionResult<DashboardSettings>> Update(DashboardSettings settings, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        user.DashboardWidgets = settings.Widgets;
        user.ShowDueAlert = settings.ShowDueAlert;
        await db.SaveChangesAsync(ct);
        return Ok(new DashboardSettings(user.DashboardWidgets, user.ShowDueAlert));
    }
}
