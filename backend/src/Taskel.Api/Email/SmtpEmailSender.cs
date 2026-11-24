using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Taskel.Api.Integrations;

namespace Taskel.Api.Email;

public class SmtpEmailSender(IntegrationSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const int TimeoutMilliseconds = 15_000;

    public Task SendReplyAsync(ReplyEmail reply, CancellationToken ct = default) =>
        SendAsync(settings.Smtp, smtp => BuildMessage(reply, smtp), "reply", ct);

    public Task SendAsync(EmailMessage message, CancellationToken ct = default) =>
        SendAsync(settings.Smtp, smtp => BuildMessage(message, smtp), "notification", ct);

    public Task SendTestAsync(EmailMessage message, SmtpOptions smtp, CancellationToken ct = default) =>
        SendAsync(smtp, options => BuildMessage(message, options), "test message", ct);

    private async Task SendAsync(SmtpOptions smtp, Func<SmtpOptions, MimeMessage> build, string kind, CancellationToken ct)
    {
        if (!smtp.IsConfigured) throw new EmailNotConfiguredException();

        try
        {
            // Built inside the try: an address the mail library refuses is a failed send, not a crash.
            var message = build(smtp);
            using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
            await client.ConnectAsync(smtp.Host!, smtp.Port, SecurityFor(smtp), ct);
            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, ct);
            await client.SendAsync(new SendRequest(message), ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending a {Kind} through {Host}:{Port} failed", kind, smtp.Host, smtp.Port);
            throw new EmailSendException("The SMTP server did not accept the message.", ex);
        }
    }

    /// <summary>TLS from the start on port 465, STARTTLS on any other port; none only when TLS is not required.</summary>
    private static SecureSocketOptions SecurityFor(SmtpOptions smtp)
    {
        if (!smtp.RequireTls) return SecureSocketOptions.None;
        return smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }

    /// <summary>Builds the reply so mail clients show it in the original email's thread.</summary>
    public static MimeMessage BuildMessage(ReplyEmail reply, SmtpOptions smtp)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromNameFor(reply.OnBehalfOf, smtp), smtp.EffectiveFromAddress!));
        if (reply.OnBehalfOf is { } person && MailboxAddress.TryParse(person.Email, out var own))
            message.ReplyTo.Add(new MailboxAddress(person.Name, own.Address));
        message.To.Add(MailboxAddress.Parse(reply.To));
        message.Subject = ReplySubject(reply.Subject);
        if (!string.IsNullOrEmpty(reply.InReplyToMessageId))
        {
            message.InReplyTo = reply.InReplyToMessageId;
            message.References.Add(reply.InReplyToMessageId);
        }
        if (reply.HtmlBody is null && (reply.Attachments is null || reply.Attachments.Count == 0))
        {
            message.Body = new TextPart("plain") { Text = reply.Body };
            return message;
        }

        var body = new BodyBuilder { TextBody = reply.Body, HtmlBody = reply.HtmlBody };
        foreach (var file in reply.Attachments ?? [])
        {
            using var content = file.Open();
            if (content is null) continue;
            var type = ContentType.TryParse(file.ContentType, out var parsed) ? parsed : new ContentType("application", "octet-stream");
            // The content is copied into the message here, so the file can be closed right after.
            body.Attachments.Add(file.FileName, content, type);
        }
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>Builds a message Taskel sends by itself. Marked as automatic so mail systems do not auto-reply to it.</summary>
    public static MimeMessage BuildMessage(EmailMessage email, SmtpOptions smtp)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(smtp.FromName) ? "Taskel" : smtp.FromName, smtp.EffectiveFromAddress!));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject.ReplaceLineEndings(" ").Trim();
        message.Headers.Add("Auto-Submitted", "auto-generated");
        message.Body = email.HtmlBody is null
            ? new TextPart("plain") { Text = email.Body }
            : new BodyBuilder { TextBody = email.Body, HtmlBody = email.HtmlBody }.ToMessageBody();
        return message;
    }

    /// <summary>"Ion Popescu prin Taskel": the person, and the name Taskel sends under ("Taskel" when none is set).</summary>
    public static string FromNameFor(Sender? person, SmtpOptions smtp)
    {
        var taskel = string.IsNullOrWhiteSpace(smtp.FromName) ? "Taskel" : smtp.FromName.Trim();
        return person is null || string.IsNullOrWhiteSpace(person.Name) ? smtp.FromName ?? string.Empty : $"{person.Name.Trim()} prin {taskel}";
    }

    private static string ReplySubject(string? original)
    {
        var subject = (original ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (subject.Length == 0) return "Re:";
        return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : $"Re: {subject}";
    }
}
