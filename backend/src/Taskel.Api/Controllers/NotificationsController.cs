using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>The signed-in user's choice about being emailed when a task reaches them.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class NotificationsController(
    TaskelDbContext db, IOptions<NotificationOptions> options, IOptions<SmtpOptions> smtp) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationSettingsResponse>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    [HttpPut]
    public async Task<ActionResult<NotificationSettingsResponse>> Update(UpdateNotificationSettingsRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        user.EmailNotifications = request.EmailOnNewTask;
        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private NotificationSettingsResponse ToResponse(User user) =>
        new(user.EmailNotifications, user.Email, options.Value.Enabled && smtp.Value.IsConfigured);
}
