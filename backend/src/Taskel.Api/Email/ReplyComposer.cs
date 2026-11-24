using System.Globalization;
using System.Net;
using System.Text;
using Taskel.Api.Models;

namespace Taskel.Api.Email;

/// <summary>
/// The reply sent when an email task is closed, as a mail thread reads: the answer on top, then (for a colleague
/// inside the company) what happened to the task in Taskel, then the original email, quoted. In plain text and in
/// HTML. Written in Romanian, like the interface.
/// </summary>
/// <remarks>
/// The history is internal: it is left out of a reply to the email's sender, who gets the answer and their own email
/// back, as with any reply.
/// </remarks>
public static class ReplyComposer
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>The time zone the history is written in. Falls back to UTC where the zone is not known.</summary>
    private static readonly TimeZoneInfo Zone = FindZone();

    private sealed record Line(DateTime At, string Who, string What, string? Text);

    /// <param name="task">The task being closed, with its people and forwards loaded.</param>
    /// <param name="closedBy">Who closes it; named in the first line when the reply goes to a colleague.</param>
    /// <param name="response">The answer.</param>
    /// <param name="messages">The task's conversation, with authors and copies loaded; used only with the history.</param>
    /// <param name="withHistory">Whether the reply goes inside the company and may show what happened in Taskel.</param>
    /// <param name="attached">Names of the files sent with the reply.</param>
    /// <param name="tooLarge">Names of answer files too large to send, kept in Taskel.</param>
    /// <param name="completed">False for an answer sent while the task stays open.</param>
    public static (string Text, string Html) Compose(TaskItem task, User closedBy, string response,
        IReadOnlyList<TaskMessage> messages, bool withHistory, IReadOnlyList<string> attached, IReadOnlyList<string> tooLarge,
        bool completed = true)
    {
        var intro = !withHistory ? null
            : completed ? $"{NameOf(closedBy)} a finalizat în Taskel task-ul „{task.Title}”:"
            : $"{NameOf(closedBy)} a răspuns în Taskel la task-ul „{task.Title}”, care rămâne deschis:";
        var history = withHistory ? History(task, messages) : [];

        var text = new StringBuilder();
        if (intro is not null) text.Append(intro).Append("\n\n");
        text.Append(response.Trim()).Append('\n');
        if (attached.Count > 0) text.Append("\nFișiere atașate: ").Append(string.Join(", ", attached)).Append('\n');
        if (tooLarge.Count > 0)
            text.Append("Prea mari pentru email, găsiți-le în Taskel: ").Append(string.Join(", ", tooLarge)).Append('\n');
        if (history.Count > 0)
        {
            text.Append("\n———— Istoricul în Taskel ————\n");
            foreach (var line in history)
            {
                text.Append(When(line.At)).Append(" · ");
                if (line.Who.Length > 0) text.Append(line.Who).Append(' ');
                text.Append(line.What).Append('\n');
                if (!string.IsNullOrWhiteSpace(line.Text))
                    foreach (var part in line.Text.Trim().Split('\n')) text.Append("    ").Append(part).Append('\n');
            }
        }
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            text.Append("\n———— Emailul original ————\n");
            foreach (var part in task.Description.Trim().Split('\n')) text.Append("> ").Append(part).Append('\n');
        }

        return (text.ToString(), Html(intro, response, attached, tooLarge, history, task.Description));
    }

    /// <summary>What happened to the task, oldest first: how it came in, forwards and returns, the conversation.</summary>
    private static List<Line> History(TaskItem task, IReadOnlyList<TaskMessage> messages)
    {
        var owner = task.Owner is null ? "—" : NameOf(task.Owner);
        var lines = new List<Line>();
        var given = task.Forwards.Count == 0 && task.Assignee is not null ? $" și i l-a dat lui {NameOf(task.Assignee)}" : "";
        lines.Add(new Line(task.CreatedAt, owner, $"a adus emailul în Taskel{given}.",
            task.Instructions is null ? null : $"Indicații: {task.Instructions}"));

        foreach (var step in task.Forwards)
        {
            var from = step.From is null ? "—" : NameOf(step.From);
            var to = step.ToUser is not null ? NameOf(step.ToUser) : step.ToGroup is not null ? $"grupul {step.ToGroup.Name}" : "—";
            lines.Add(new Line(step.At, from,
                step.IsReturn ? $"a rezolvat și i-a trimis task-ul înapoi lui {to}:" : $"a direcționat task-ul către {to}.", step.Note));
        }

        foreach (var message in messages)
        {
            var who = message.Author is null ? "Taskel" : NameOf(message.Author);
            var to = message.RelatedTask is { } copy
                ? copy.Assignee is not null ? NameOf(copy.Assignee) : copy.Group?.Name ?? "—"
                : null;
            var what = message.Kind switch
            {
                TaskMessageKind.Distributed => $"a distribuit task-ul către {to}.",
                TaskMessageKind.Answered => "a răspuns:",
                TaskMessageKind.AllAnswered => "Au răspuns toți.",
                TaskMessageKind.SentBack => $"i-a trimis înapoi lui {to}:",
                TaskMessageKind.Closed => $"a închis task-ul lui {to}.",
                TaskMessageKind.DueDateChanged => "a schimbat termenul:",
                TaskMessageKind.Rescheduled => "a replanificat task-ul:",
                TaskMessageKind.Replied when message.SentTo is not null => $"a răspuns pe email către {message.SentTo}:",
                TaskMessageKind.Replied => "a răspuns:",
                _ => to is not null && to != who ? $"către {to}:" : "a scris:"
            };
            lines.Add(new Line(message.At, message.Kind == TaskMessageKind.AllAnswered ? "" : who, what, message.Text));
        }
        return [.. lines.OrderBy(l => l.At)];
    }

    private static string Html(string? intro, string response, IReadOnlyList<string> attached, IReadOnlyList<string> tooLarge,
        IReadOnlyList<Line> history, string? original)
    {
        const string font = "font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#111";
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;").Append(font).Append("\">");
        if (intro is not null) html.Append("<p style=\"margin:0 0 12px;color:#555\">").Append(E(intro)).Append("</p>");
        html.Append("<div style=\"margin:0 0 16px\">").Append(Paragraphs(response)).Append("</div>");
        if (attached.Count > 0)
        {
            html.Append("<p style=\"margin:0 0 8px;color:#555\">Fișiere atașate: <b>")
                .Append(E(string.Join(", ", attached))).Append("</b></p>");
        }
        if (tooLarge.Count > 0)
        {
            html.Append("<p style=\"margin:0 0 8px;color:#8a5300\">Prea mari pentru email, găsiți-le în Taskel: ")
                .Append(E(string.Join(", ", tooLarge))).Append("</p>");
        }

        if (history.Count > 0)
        {
            html.Append("<div style=\"margin:20px 0 0;padding:12px 0 0;border-top:1px solid #ddd\">")
                .Append("<p style=\"margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:#666\">")
                .Append("Istoricul în Taskel</p>");
            foreach (var line in history)
            {
                html.Append("<div style=\"margin:0 0 10px\"><span style=\"color:#777;font-size:12px\">").Append(E(When(line.At)))
                    .Append("</span><br>");
                if (line.Who.Length > 0) html.Append("<b>").Append(E(line.Who)).Append("</b> ");
                html.Append(E(line.What));
                if (!string.IsNullOrWhiteSpace(line.Text))
                {
                    html.Append("<div style=\"margin:4px 0 0;padding:6px 10px;background:#f4f5f7;border-radius:6px\">")
                        .Append(Paragraphs(line.Text)).Append("</div>");
                }
                html.Append("</div>");
            }
            html.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(original))
        {
            html.Append("<div style=\"margin:20px 0 0;padding:12px 0 0;border-top:1px solid #ddd\">")
                .Append("<p style=\"margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:#666\">")
                .Append("Emailul original</p>")
                .Append("<blockquote style=\"margin:0;padding:0 0 0 12px;border-left:3px solid #c8ccd4;color:#444\">")
                .Append(Paragraphs(original)).Append("</blockquote></div>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string Paragraphs(string text) => E(text.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>");

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string When(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("d MMM yyyy, HH:mm", Romanian);

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
