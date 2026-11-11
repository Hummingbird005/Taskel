using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Taskel.Api.Email;

public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const int TimeoutMilliseconds = 15_000;

    public async Task SendReplyAsync(ReplyEmail reply, CancellationToken ct = default)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured) throw new EmailNotConfiguredException();

        var message = BuildMessage(reply, smtp);
        try
        {
            using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
            var security = smtp.RequireTls
                ? (smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;
            await client.ConnectAsync(smtp.Host!, smtp.Port, security, ct);
            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, ct);
            await client.SendAsync(new SendRequest(message), ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending a reply through {Host}:{Port} failed", smtp.Host, smtp.Port);
            throw new EmailSendException("The SMTP server did not accept the message.", ex);
        }
    }

    /// <summary>Builds the reply so mail clients show it in the original email's thread.</summary>
    public static MimeMessage BuildMessage(ReplyEmail reply, SmtpOptions smtp)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp.FromName ?? string.Empty, smtp.EffectiveFromAddress!));
        message.To.Add(MailboxAddress.Parse(reply.To));
        message.Subject = ReplySubject(reply.Subject);
        if (!string.IsNullOrEmpty(reply.InReplyToMessageId))
        {
            message.InReplyTo = reply.InReplyToMessageId;
            message.References.Add(reply.InReplyToMessageId);
        }
        message.Body = new TextPart("plain") { Text = reply.Body };
        return message;
    }

    private static string ReplySubject(string? original)
    {
        var subject = (original ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (subject.Length == 0) return "Re:";
        return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : $"Re: {subject}";
    }
}
