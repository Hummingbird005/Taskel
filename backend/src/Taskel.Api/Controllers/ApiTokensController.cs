using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Auth;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>
/// Manages the signed-in user's API tokens. Requires the browser session:
/// an API token cannot be used to list, create or revoke tokens.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Route("api/auth/tokens")]
public class ApiTokensController(TaskelDbContext db) : ControllerBase
{
    public const int MaxTokensPerUser = 10;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiTokenResponse>>> GetAll(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return Ok(await db.ApiTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new ApiTokenResponse(t.Id, t.Name, t.CreatedAt, t.LastUsedAt))
            .ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<CreatedApiTokenResponse>> Create(CreateApiTokenRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (await db.ApiTokens.CountAsync(t => t.UserId == userId, ct) >= MaxTokensPerUser)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"You can have at most {MaxTokensPerUser} API tokens. Revoke one first.");
        }

        var token = ApiTokens.Generate();
        var entity = new ApiToken { UserId = userId, Name = request.Name.Trim(), TokenHash = ApiTokens.Hash(token) };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new CreatedApiTokenResponse(entity.Id, entity.Name, entity.CreatedAt, token));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var deleted = await db.ApiTokens.Where(t => t.Id == id && t.UserId == userId).ExecuteDeleteAsync(ct);
        return deleted > 0 ? NoContent() : NotFound();
    }
}
