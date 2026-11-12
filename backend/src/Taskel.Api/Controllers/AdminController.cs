using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>Roles and departments, for administrators.</summary>
[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController(TaskelDbContext db, CurrentUser currentUser, IOptions<AuthOptions> authOptions) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<AdminUserResponse>>> GetUsers(CancellationToken ct)
    {
        if (await RequireAdminAsync(ct) is { } refusal) return refusal;

        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        return Ok(users.Select(ToResponse)
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Email, StringComparer.Ordinal));
    }

    [HttpPut("users/{id:guid}")]
    public async Task<ActionResult<AdminUserResponse>> UpdateUser(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        if (await RequireAdminAsync(ct) is { } refusal) return refusal;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        // Stops the last administrator from locking everyone out by mistake.
        if (user.Id == User.GetUserId() && request.Role != authOptions.Value.EffectiveRole(user))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "You cannot change your own role. Ask another administrator.");
        }

        if (!authOptions.Value.IsConfiguredAdmin(user)) user.Role = request.Role;
        user.Department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private AdminUserResponse ToResponse(User user) => new(
        user.Id, user.Email, user.DisplayName ?? user.Email, authOptions.Value.EffectiveRole(user), user.Department,
        user.DirectoryId is not null, authOptions.Value.IsConfiguredAdmin(user));

    private async Task<ActionResult?> RequireAdminAsync(CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        return me.Value.Role == UserRole.Admin
            ? null
            : Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only administrators can manage users.");
    }
}
