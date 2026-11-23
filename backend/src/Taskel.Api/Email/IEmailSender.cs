namespace Taskel.Api.Email;

/// <summary>A reply to an email that was turned into a task.</summary>
/// <param name="To">Who gets it: the original sender, or the person who brought the email into Taskel.</param>
/// <param name="Subject">Subject of the original email, without a "Re:" prefix added.</param>
/// <param name="Body">The plain-text version.</param>
/// <param name="InReplyToMessageId">Message-ID of the original email, when known.</param>
/// <param name="HtmlBody">The same, formatted, for mail programs that show HTML.</param>
/// <param name="Attachments">Files sent with the reply.</param>
public record ReplyEmail(string To, string? Subject, string Body, string? InReplyToMessageId, string? HtmlBody = null,
    IReadOnlyList<ReplyAttachment>? Attachments = null);

/// <summary>A file attached to a reply; opened when the message is built.</summary>
public record ReplyAttachment(string FileName, string ContentType, Func<Stream?> Open);

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
