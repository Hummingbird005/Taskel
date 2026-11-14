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
using Taskel.Api.Integrations;
using Taskel.Api.Keycloak;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    TaskelDbContext db,
    IPasswordHasher<User> passwordHasher,
    IntegrationSettings settings,
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
        var ad = settings.Directory;
        var keycloak = settings.Keycloak;
        return Ok(new AuthOptionsResponse(authOptions.Value.AllowLocalRegistration, ad.IsUsable, ad.IsUsable ? ad.Domain : null,
            keycloak.IsUsable, keycloak.IsUsable ? keycloak.DisplayName : null));
    }

    /// <summary>
    /// Starts a Keycloak sign-in: the browser is sent to Keycloak and comes back to
    /// <see cref="KeycloakOptions.CallbackPath"/>, where <see cref="KeycloakOidcEvents"/> opens the session.
    /// </summary>
    [HttpGet("keycloak/login")]
    [EnableRateLimiting(LoginRateLimit)]
    public IActionResult LoginWithKeycloak()
    {
        if (!settings.Keycloak.IsUsable) return NotFound();
        return Challenge(new AuthenticationProperties { RedirectUri = PublicAddress.HomeOf(Request, settings) }, KeycloakOptions.Scheme);
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
        var hasLocalPassword = user is not null && !user.IsExternal && user.PasswordHash.Length > 0;
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
    public async Task<ActionResult<UserResponse>> LoginWithDirectory(
        DirectoryLoginRequest request,
        [FromServices] IDirectoryAuthenticator directory,
        [FromServices] DirectoryAccounts directoryAccounts,
        CancellationToken ct)
    {
        if (!settings.Directory.IsUsable) return NotFound();

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

    /// <summary>
    /// Ends the Taskel session. For a Keycloak session it also answers with the address that ends
    /// the Keycloak session, for the browser to go to; otherwise signing in again would be silent.
    /// </summary>
    [HttpPost("logout")]
    public async Task<ActionResult<LogoutResponse>> Logout([FromServices] KeycloakLogout keycloakLogout, CancellationToken ct)
    {
        string? logoutUrl = null;
        if (User.FindFirst(KeycloakOidcEvents.ProviderClaim)?.Value == KeycloakOptions.Scheme && settings.Keycloak.IsUsable)
        {
            var idToken = await HttpContext.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, KeycloakOidcEvents.IdTokenName);
            logoutUrl = await keycloakLogout.AddressAsync(Request, idToken, ct);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return logoutUrl is null ? NoContent() : Ok(new LogoutResponse(logoutUrl));
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
