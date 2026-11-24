using System.Globalization;
using System.Net;
using System.Text;
using Taskel.Api.Models;

namespace Taskel.Api.Email;

/// <summary>
/// The reply sent for an email task, written as a mail program writes one: the answer on top, then what came before
/// it, quoted, newest first ("Pe 24 nov. 2026, la 11:30, Andrei Stan a scris:"), down to the original email. In plain
/// text and in HTML. Written in Romanian, like the interface.
/// </summary>
/// <remarks>
/// What was said in Taskel is internal: it is quoted only in a reply to a colleague inside the company. The email's
/// sender gets the answer and their own email back, as with any reply.
/// </remarks>
public static class ReplyComposer
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>The time zone dates are written in. Falls back to UTC where the zone is not known.</summary>
    private static readonly TimeZoneInfo Zone = FindZone();

    /// <summary>One message quoted under the answer: who wrote it, when, in what role, and what.</summary>
    private sealed record Quote(DateTime At, string Header, string Text);

    /// <param name="task">The task being answered, with its people and forwards loaded.</param>
    /// <param name="closedBy">Who answers; named in the first line when the reply goes to a colleague.</param>
    /// <param name="response">The answer.</param>
    /// <param name="messages">The task's conversation, with authors and copies loaded; quoted only with the history.</param>
    /// <param name="withHistory">Whether the reply goes inside the company and may quote what was said in Taskel.</param>
    /// <param name="attached">Names of the files sent with the reply.</param>
    /// <param name="tooLarge">Names of answer files too large to send, kept in Taskel.</param>
    /// <param name="completed">False for an answer sent while the task stays open.</param>
    public static (string Text, string Html) Compose(TaskItem task, User closedBy, string response,
        IReadOnlyList<TaskMessage> messages, bool withHistory, IReadOnlyList<string> attached, IReadOnlyList<string> tooLarge,
        bool completed = true)
    {
        var intro = !withHistory ? null
            : completed ? $"{NameOf(closedBy)} a finalizat în Taskel task-ul „{task.Title}”."
            : $"{NameOf(closedBy)} a răspuns în Taskel la task-ul „{task.Title}”, care rămâne deschis.";
        var quotes = withHistory ? History(task, messages) : [];
        var original = OriginalEmail.Parse(task.Description, task.SourceEmailAddress);
        if (original is not null && original.Body.Length > 0)
        {
            var when = original.Received is { } received ? $"Pe {Day(received)}, la {received:HH:mm}, " : "";
            quotes.Add(new Quote(DateTime.MinValue, $"{Capital(when)}{original.Who ?? "expeditorul"} a scris:", original.Body));
        }

        var text = new StringBuilder();
        if (intro is not null) text.Append("✓ ").Append(intro).Append("\n\n");
        text.Append(response.Trim()).Append('\n');
        if (attached.Count > 0) text.Append("\nFișiere atașate: ").Append(string.Join(", ", attached)).Append('\n');
        if (tooLarge.Count > 0)
            text.Append("Prea mari pentru email, găsiți-le în Taskel: ").Append(string.Join(", ", tooLarge)).Append('\n');
        foreach (var quote in quotes)
        {
            text.Append('\n').Append(quote.Header).Append('\n');
            foreach (var line in quote.Text.Trim().Split('\n')) text.Append("> ").Append(line).Append('\n');
        }

        return (text.ToString(), Html(intro, response, attached, tooLarge, quotes));
    }

    /// <summary>What was written in Taskel before the answer, newest first; events without text are left out.</summary>
    private static List<Quote> History(TaskItem task, IReadOnlyList<TaskMessage> messages)
    {
        var quotes = new List<Quote>();
        if (task.Instructions is { } instructions && task.Owner is { } owner)
            quotes.Add(Said(task.CreatedAt, NameOf(owner), "indicații", instructions));

        foreach (var step in task.Forwards.Where(f => !string.IsNullOrWhiteSpace(f.Note)))
        {
            var to = step.ToUser is not null ? NameOf(step.ToUser) : step.ToGroup is not null ? $"grupul {step.ToGroup.Name}" : "—";
            quotes.Add(Said(step.At, step.From is null ? "—" : NameOf(step.From),
                step.IsReturn ? $"rezolvat, trimis înapoi lui {to}" : $"direcționat către {to}", step.Note!));
        }

        // A distribution to several people is one message.
        foreach (var group in messages.Where(m => !string.IsNullOrWhiteSpace(m.Text))
                     .GroupBy(m => m.Kind == TaskMessageKind.Distributed ? $"d|{m.AuthorId}|{m.Text}" : m.Id.ToString()))
        {
            var message = group.First();
            var who = message.Author is null ? "Taskel" : NameOf(message.Author);
            var to = string.Join(", ", group.Select(m => m.RelatedTask is { } copy
                ? copy.Assignee is not null ? NameOf(copy.Assignee) : copy.Group?.Name ?? "—"
                : null).Where(n => n is not null));
            var role = message.Kind switch
            {
                TaskMessageKind.Distributed => $"distribuit către {to}",
                TaskMessageKind.Answered => "răspuns în Taskel",
                TaskMessageKind.Replied when message.SentTo is not null => $"trimis pe email către {Addressee(task, message.SentTo)}",
                TaskMessageKind.Replied => "răspuns în Taskel",
                TaskMessageKind.SentBack => $"trimis înapoi lui {to}",
                TaskMessageKind.Rescheduled or TaskMessageKind.DueDateChanged => "replanificare",
                _ => to.Length > 0 && to != who ? $"către {to}" : "notă în Taskel"
            };
            quotes.Add(Said(message.At, who, role, message.Text!));
        }
        return [.. quotes.OrderByDescending(q => q.At)];
    }

    /// <summary>The colleague an address belongs to, by name; the address itself for anyone else.</summary>
    private static string Addressee(TaskItem task, string address) =>
        task.Owner is { } owner && string.Equals(owner.Email, address, StringComparison.OrdinalIgnoreCase) ? NameOf(owner) : address;

    private static Quote Said(DateTime utc, string who, string role, string text)
    {
        var local = Local(utc);
        return new Quote(utc, $"Pe {Day(local)}, la {local:HH:mm}, {who} a scris ({role}):", text);
    }

    private static string Html(string? intro, string response, IReadOnlyList<string> attached, IReadOnlyList<string> tooLarge,
        IReadOnlyList<Quote> quotes)
    {
        const string font = "font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.55;color:#1b1b1f";
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;").Append(font).Append("\">");
        if (intro is not null)
        {
            html.Append("<p style=\"margin:0 0 16px;padding:8px 12px;border-radius:6px;background:#f4f5f7;color:#55555c;font-size:13px\">")
                .Append("&#10003; ").Append(E(intro)).Append("</p>");
        }
        html.Append("<div style=\"margin:0 0 16px\">").Append(Paragraphs(response)).Append("</div>");
        if (attached.Count > 0)
        {
            html.Append("<p style=\"margin:0 0 8px;color:#55555c;font-size:13px\">Fișiere atașate: ")
                .Append(E(string.Join(", ", attached))).Append("</p>");
        }
        if (tooLarge.Count > 0)
        {
            html.Append("<p style=\"margin:0 0 8px;color:#8a5300;font-size:13px\">Prea mari pentru email, găsiți-le în Taskel: ")
                .Append(E(string.Join(", ", tooLarge))).Append("</p>");
        }
        foreach (var quote in quotes)
        {
            html.Append("<p style=\"margin:20px 0 6px;color:#55555c;font-size:13px\">").Append(E(quote.Header)).Append("</p>")
                .Append("<blockquote style=\"margin:0;padding:0 0 0 12px;border-left:3px solid #c8ccd4;color:#44444b\">")
                .Append(Paragraphs(quote.Text)).Append("</blockquote>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string Paragraphs(string text) => E(text.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>");

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Romanian) + text[1..];

    /// <summary>"24 nov. 2026".</summary>
    private static string Day(DateTime local) => local.ToString("d MMM yyyy", Romanian);

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    private static string NameOf(User user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
