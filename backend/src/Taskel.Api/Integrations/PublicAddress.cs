namespace Taskel.Api.Integrations;

/// <summary>Where people reach the Taskel interface: links in emails, and the return address after a Keycloak sign-in.</summary>
public static class PublicAddress
{
    /// <summary>
    /// The configured public address (<c>Email:Notifications:PublicUrl</c>, or the email settings in the
    /// interface), otherwise the address this request came to. Never ends with a slash.
    /// </summary>
    public static string Of(HttpRequest request, IntegrationSettings settings) =>
        Parse(settings.Notifications.PublicUrl) ?? $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');

    /// <summary>The interface's home page: <see cref="Of"/> with a trailing slash.</summary>
    public static string HomeOf(HttpRequest request, IntegrationSettings settings) => $"{Of(request, settings)}{PathSeparator}";

    private const char PathSeparator = '/';

    /// <summary>The address without a trailing slash, or null when it is not an absolute http(s) address without query or fragment.</summary>
    public static string? Parse(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;
}
