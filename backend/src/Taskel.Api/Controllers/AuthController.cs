using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Taskel.Api.Data;
using Taskel.Api.Directory;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    TaskelDbContext db,
    IPasswordHasher<User> passwordHasher,
    IDirectoryAuthenticator directory,
    DirectoryAccounts directoryAccounts,
    IOptions<DirectoryOptions> directoryOptions,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    public const string LoginRateLimit = "login";

    // Verified when the email is unknown, so login takes similar time whether or not the account exists.
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, "taskel-dummy-password");

    /// <summary>What the sign-in screen should offer. Public, so it can be read before signing in.</summary>
    [HttpGet("options")]
    public ActionResult<AuthOptionsResponse> Options()
    {
        var ad = directoryOptions.Value;
        return Ok(new AuthOptionsResponse(authOptions.Value.AllowLocalRegistration, ad.IsUsable, ad.IsUsable ? ad.Domain : null));
    }

    [HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!authOptions.Value.AllowLocalRegistration)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Creating local accounts is turned off.");

        var email = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return EmailTaken();

        var user = new User { Email = email };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent registration for the same email.
            return EmailTaken();
        }

        await SignInAsync(user);
        return Ok(ToResponse(user));
    }

    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimit)]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Directory accounts have no local password; they sign in through the directory.
        var hasLocalPassword = user is not null && user.DirectoryId is null && user.PasswordHash.Length > 0;
        var result = hasLocalPassword
            ? passwordHasher.VerifyHashedPassword(user!, user!.PasswordHash, request.Password)
            : passwordHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
        if (!hasLocalPassword || result == PasswordVerificationResult.Failed)
            return InvalidCredentials("Invalid email or password.");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        await SignInAsync(user!);
        return Ok(ToResponse(user!));
    }

    /// <summary>Signs in with an Active Directory account. The Taskel account is created on first use.</summary>
    [HttpPost("login/directory")]
    [EnableRateLimiting(LoginRateLimit)]
    public async Task<ActionResult<UserResponse>> LoginWithDirectory(DirectoryLoginRequest request, CancellationToken ct)
    {
        if (!directoryOptions.Value.IsUsable) return NotFound();

        DirectoryUser? directoryUser;
        try
        {
            directoryUser = await directory.AuthenticateAsync(request.Username, request.Password, ct);
        }
        catch (DirectoryUnavailableException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Active Directory is not available right now. Try again later.");
        }
        if (directoryUser is null) return InvalidCredentials("Invalid user name or password.");

        var user = await directoryAccounts.GetOrCreateAsync(directoryUser, ct);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Another directory account already uses this email address in Taskel.");
        }

        await SignInAsync(user);
        return Ok(ToResponse(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            // The account no longer exists; drop the stale session.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized();
        }
        return Ok(ToResponse(user));
    }

    private UserResponse ToResponse(User user) => UserResponse.From(user, authOptions.Value.EffectiveRole(user));

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private ObjectResult EmailTaken() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "An account with this email already exists.");

    private ObjectResult InvalidCredentials(string title) =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: title);

    private Task SignInAsync(User user)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
