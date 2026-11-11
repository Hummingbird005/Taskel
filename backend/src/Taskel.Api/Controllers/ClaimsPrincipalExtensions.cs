using System.Security.Claims;

namespace Taskel.Api.Controllers;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Id of the signed-in user. Only call on authenticated requests.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new InvalidOperationException("Authenticated user has no id claim."));
}
