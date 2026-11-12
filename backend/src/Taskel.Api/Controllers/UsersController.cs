using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>The people a task can be assigned to: every account, by name and email only.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController(TaskelDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSummary>>> GetAll(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        return Ok(users.Select(UserSummary.From)
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Email, StringComparer.Ordinal));
    }
}
