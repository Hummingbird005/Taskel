using System.Globalization;
using MimeKit;

namespace Taskel.Api.Email;

/// <summary>
/// The email a task was made from, read back from the task's message: its first lines say who sent it
/// ("De la: Ana Popa &lt;ana@client.example&gt;") and when it came ("Primit: 2026-11-23 09:12"), then the text.
/// </summary>
/// <param name="SenderName">The sender's name, or their address when the email gave no name.</param>
/// <param name="SenderAddress">The sender's address, when known.</param>
/// <param name="Received">When the email came, in the sender's local time, as Outlook gave it.</param>
/// <param name="Body">The email's text, without those first lines.</param>
public record OriginalEmail(string? SenderName, string? SenderAddress, DateTime? Received, string Body)
{
    /// <summary>
    /// Reads the task's message. <paramref name="fallbackAddress"/> names the sender when the message does not.
    /// Null when there is no message.
    /// </summary>
    public static OriginalEmail? Parse(string? description, string? fallbackAddress)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var lines = description.Replace("\r\n", "\n").Split('\n').ToList();
        string? sender = null;
        DateTime? received = null;
        var used = 0;
        for (; used < lines.Count && used < 2; used++)
        {
            var line = lines[used];
            if (line.StartsWith("De la: ", StringComparison.Ordinal)) sender = line["De la: ".Length..].Trim();
            else if (line.StartsWith("Primit: ", StringComparison.Ordinal)
                     && DateTime.TryParseExact(line["Primit: ".Length..].Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var at))
                received = at;
            else break;
        }
        var body = string.Join('\n', lines.Skip(used)).Trim();

        string? name = null, address = fallbackAddress;
        if (sender is not null && MailboxAddress.TryParse(sender, out var mailbox))
        {
            name = string.IsNullOrWhiteSpace(mailbox.Name) ? null : mailbox.Name.Trim();
            address = mailbox.Address;
        }
        else if (sender is not null)
        {
            name = sender;
        }
        return new OriginalEmail(name ?? address, address, received, body);
    }

    /// <summary>"Ana Popa &lt;ana@client.example&gt;", or whichever of the two is known.</summary>
    public string? Who =>
        SenderName is null ? SenderAddress
        : SenderAddress is null || SenderAddress == SenderName ? SenderName
        : $"{SenderName} <{SenderAddress}>";
}
