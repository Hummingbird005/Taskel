using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>
/// The signed-in account as it is now. Roles and departments are read from the database on each
/// request rather than from the session, so a change takes effect at once.
/// </summary>
public class CurrentUser(TaskelDbContext db, IOptions<AuthOptions> authOptions)
{
    public async Task<(User User, UserRole Role)?> GetAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = principal.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : (user, authOptions.Value.EffectiveRole(user));
    }
}
