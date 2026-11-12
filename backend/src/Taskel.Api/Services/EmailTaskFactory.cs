using System.Globalization;
using System.Text.RegularExpressions;
using MimeKit;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>
/// Builds a task from an email, shortening the subject and body to the task's limits.
/// The text it writes into the task (note labels, placeholder title) is Romanian, like the interface.
/// </summary>
public static partial class EmailTaskFactory
{
    public const string EmailTag = "email";
    public const string NoSubjectTitle = "(fără subiect)";
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxSourceSubjectLength = 500;

    public static TaskItem Create(Guid userId, CreateTaskFromEmailRequest email)
    {
        var title = Whitespace().Replace(email.Subject ?? string.Empty, " ").Trim();

        var header = new List<string>();
        if (!string.IsNullOrWhiteSpace(email.Sender))
            header.Add($"De la: {Whitespace().Replace(email.Sender, " ").Trim()}");
        if (email.ReceivedAt is { } received)
            header.Add($"Primit: {received.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

        var body = (email.Body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        body = BlankLines().Replace(body, "\n\n").Trim();

        var description = string.Join("\n\n",
            new[] { string.Join("\n", header), body }.Where(part => part.Length > 0));

        var replyAddress = ReplyAddress(email);

        return new TaskItem
        {
            UserId = userId,
            Title = Truncate(title.Length > 0 ? title : NoSubjectTitle, MaxTitleLength),
            Description = description.Length > 0 ? Truncate(description, MaxDescriptionLength) : null,
            DueDate = email.DueDate,
            Priority = email.Importance switch
            {
                EmailImportance.High => TaskPriority.High,
                EmailImportance.Low => TaskPriority.Low,
                _ => TaskPriority.Medium
            },
            Tags = [EmailTag],
            SourceEmailAddress = replyAddress,
            SourceEmailSubject = replyAddress is null ? null : Truncate(title, MaxSourceSubjectLength),
            SourceEmailMessageId = replyAddress is null ? null : NormalizeMessageId(email.MessageId)
        };
    }

    /// <summary>
    /// The address a completion reply goes to: the explicit sender address, or else one found in
    /// the display form "Name &lt;address&gt;". Null when there is no usable address.
    /// </summary>
    private static string? ReplyAddress(CreateTaskFromEmailRequest email)
    {
        foreach (var candidate in new[] { email.SenderEmail, email.Sender })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (MailboxAddress.TryParse(candidate.Trim(), out var mailbox) && IsPlainAddress(mailbox.Address))
                return mailbox.Address;
        }
        return null;
    }

    private static bool IsPlainAddress(string address) =>
        address.Length <= 320 && PlainAddress().IsMatch(address);

    /// <summary>Keeps a Message-ID only in its strict "&lt;id@host&gt;" form, so it is safe to put in a header.</summary>
    private static string? NormalizeMessageId(string? messageId)
    {
        var value = messageId?.Trim();
        return value is not null && MessageId().IsMatch(value) ? value : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)].TrimEnd() + "…";

    [GeneratedRegex(@"^[^\s<>@,;]+@[^\s<>@,;]+\.[^\s<>@,;]+$")]
    private static partial Regex PlainAddress();

    [GeneratedRegex(@"^<[^\s<>@]+@[^\s<>@]+>$")]
    private static partial Regex MessageId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex BlankLines();
}
