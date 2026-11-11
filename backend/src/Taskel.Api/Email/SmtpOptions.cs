namespace Taskel.Api.Email;

/// <summary>Bound from the <c>Email:Smtp</c> configuration section.</summary>
public class SmtpOptions
{
    public const string Section = "Email:Smtp";

    public string? Host { get; set; }
    /// <summary>587 uses STARTTLS, 465 uses TLS from the start.</summary>
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    /// <summary>Set through an environment variable or user secrets, never in appsettings.json.</summary>
    public string? Password { get; set; }
    /// <summary>The mailbox replies are sent from. Defaults to <see cref="Username"/>.</summary>
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    /// <summary>Only for local test servers that do not offer TLS. Never disable for a real mailbox.</summary>
    public bool RequireTls { get; set; } = true;

    public string? EffectiveFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(EffectiveFromAddress);
}
