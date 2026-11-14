using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Taskel.Api.Integrations;

namespace Taskel.Api.Keycloak;

/// <summary>
/// Sets up the OpenID Connect handler from the current Keycloak settings. The handler keeps its
/// options once built; <see cref="IntegrationSettings"/> drops them whenever the settings change.
/// </summary>
public sealed class KeycloakOidcSetup(IntegrationSettings settings, IWebHostEnvironment environment)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    /// <summary>
    /// Stands in while Keycloak is not set up: the handler checks its options on every request, and
    /// these pass the check without ever being contacted (sign-in is refused before that).
    /// </summary>
    private const string PlaceholderAuthority = "https://keycloak.invalid/realms/not-configured";

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != KeycloakOptions.Scheme) return;

        var keycloak = settings.Keycloak;
        var usable = keycloak.IsUsable;
        options.Authority = usable ? keycloak.Authority!.Trim().TrimEnd('/') : PlaceholderAuthority;
        options.ClientId = usable ? keycloak.ClientId!.Trim() : "not-configured";
        options.ClientSecret = usable && !string.IsNullOrEmpty(keycloak.ClientSecret) ? keycloak.ClientSecret : null;
        options.RequireHttpsMetadata = !usable || keycloak.RequireHttpsMetadata;

        options.CallbackPath = KeycloakOptions.CallbackPath;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = OpenIdConnectResponseType.Code;
        // The code comes back on a plain GET, so the correlation cookies can stay SameSite=Lax.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        options.DisableTelemetry = true;
        options.BackchannelTimeout = TimeSpan.FromSeconds(15);
        options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
        var secure = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = secure;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = secure;
        options.EventsType = typeof(KeycloakOidcEvents);
    }
}

/// <summary>Turns a Keycloak sign-in into a Taskel session, or sends the browser back with the reason it was refused.</summary>
public sealed class KeycloakOidcEvents(
    IntegrationSettings settings,
    KeycloakAccounts accounts,
    ILogger<KeycloakOidcEvents> logger) : OpenIdConnectEvents
{
    /// <summary>Marks a session that came from Keycloak, so signing out can end the Keycloak session too.</summary>
    public const string ProviderClaim = "taskel:provider";
    public const string IdTokenName = "id_token";

    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        if (!settings.Keycloak.IsUsable)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.HandleResponse();
            return Task.CompletedTask;
        }
        // Behind a proxy (or Vite in development) the API does not know the address the browser uses.
        context.ProtocolMessage.RedirectUri = RedirectUri(context.Request, settings);
        return Task.CompletedTask;
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var options = settings.Keycloak;
        var account = context.Principal is { } principal
            ? KeycloakClaims.Read(principal, context.TokenEndpointResponse?.AccessToken, options)
            : null;
        if (account is null)
        {
            logger.LogWarning("Keycloak returned an ID token without a usable subject");
            Refuse(context, KeycloakRefusal.NoEmail);
            return;
        }

        var link = await accounts.SignInAsync(account, options, context.HttpContext.RequestAborted);
        if (link.User is not { } user)
        {
            logger.LogWarning("Keycloak sign-in of {Subject} refused: {Reason}", account.Subject, link.Refusal);
            Refuse(context, link.Refusal ?? KeycloakRefusal.NoEmail);
            return;
        }

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ProviderClaim, KeycloakOptions.Scheme)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        var idToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken;
        if (!string.IsNullOrEmpty(idToken))
            context.Properties!.StoreTokens([new AuthenticationToken { Name = IdTokenName, Value = idToken }]);
        context.Properties!.IsPersistent = true;
        context.Properties.RedirectUri = PublicAddress.HomeOf(context.Request, settings);
    }

    public override Task RemoteFailure(RemoteFailureContext context)
    {
        logger.LogWarning(context.Failure, "Keycloak sign-in failed");
        context.Response.Redirect(ErrorAddress(context.Request, "keycloak"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    public override Task AccessDenied(AccessDeniedContext context)
    {
        context.Response.Redirect(ErrorAddress(context.Request, "keycloak-cancelled"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>The address Keycloak sends the browser back to; it must be listed in the client's valid redirect URIs.</summary>
    public static string RedirectUri(HttpRequest request, IntegrationSettings settings) =>
        PublicAddress.Of(request, settings) + KeycloakOptions.CallbackPath;

    private void Refuse(TokenValidatedContext context, KeycloakRefusal refusal)
    {
        var code = refusal switch
        {
            KeycloakRefusal.EmailTaken => "keycloak-email-taken",
            KeycloakRefusal.EmailUnverified => "keycloak-email-unverified",
            KeycloakRefusal.RoleMissing => "keycloak-role",
            _ => "keycloak-no-email"
        };
        context.Response.Redirect(ErrorAddress(context.Request, code));
        context.HandleResponse();
    }

    private string ErrorAddress(HttpRequest request, string code) =>
        $"{PublicAddress.Of(request, settings)}/?loginError={code}";
}
