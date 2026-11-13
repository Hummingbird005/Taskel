namespace Taskel.Api.Email;

/// <summary>A plain-text reply to an email that was turned into a task.</summary>
/// <param name="To">Address of the original sender.</param>
/// <param name="Subject">Subject of the original email, without a "Re:" prefix added.</param>
/// <param name="Body">The response text.</param>
/// <param name="InReplyToMessageId">Message-ID of the original email, when known.</param>
public record ReplyEmail(string To, string? Subject, string Body, string? InReplyToMessageId);

/// <summary>A plain-text email Taskel sends on its own behalf, such as a notification.</summary>
public record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    /// <exception cref="EmailNotConfiguredException">No SMTP server is configured.</exception>
    /// <exception cref="EmailSendException">The message could not be handed to the SMTP server.</exception>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);

    /// <exception cref="EmailNotConfiguredException">No SMTP server is configured.</exception>
    /// <exception cref="EmailSendException">The message could not be handed to the SMTP server.</exception>
    Task SendReplyAsync(ReplyEmail reply, CancellationToken ct = default);

    /// <summary>Sends through the given server settings instead of the saved ones, to try them before saving.</summary>
    /// <exception cref="EmailNotConfiguredException">The settings name no server or sender.</exception>
    /// <exception cref="EmailSendException">The message could not be handed to the SMTP server.</exception>
    Task SendTestAsync(EmailMessage message, SmtpOptions smtp, CancellationToken ct = default);
}

public class EmailNotConfiguredException() : Exception("Email sending is not configured.");

public class EmailSendException(string message, Exception inner) : Exception(message, inner);
