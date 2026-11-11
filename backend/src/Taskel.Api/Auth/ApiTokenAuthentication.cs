using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;

namespace Taskel.Api.Auth;

public static class ApiTokens
{
    public const string Scheme = "ApiToken";
    public const string Prefix = "tsk_";

    /// <summary>A new random token: the prefix plus 256 bits of randomness.</summary>
    public static string Generate() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Tokens are high-entropy random values, so a plain SHA-256 is enough to store them safely.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Authenticates requests carrying <c>Authorization: Bearer tsk_…</c>.</summary>
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TaskelDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !"Bearer".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiTokens.Hash(header.Parameter);
        var token = await db.ApiTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, Context.RequestAborted);
        if (token is null)
            return AuthenticateResult.Fail("Unknown or revoked API token.");

        // Record usage, but not more than once a minute per token.
        var now = DateTime.UtcNow;
        if (token.LastUsedAt is null || now - token.LastUsedAt > LastUsedPrecision)
        {
            await db.ApiTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, token.UserId.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
