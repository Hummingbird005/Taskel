using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Integrations;
using Taskel.Api.Models;

namespace Taskel.Api.Email;

/// <summary>Bound from the <c>Email:Notifications</c> configuration section.</summary>
public class NotificationOptions
{
    public const string Section = "Email:Notifications";

    /// <summary>Whether Taskel emails people when they receive a task. Each user can also turn it off for themselves.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Address of the Taskel web interface, e.g. <c>https://taskel.example.com</c>. Added to the emails as a link when set.</summary>
    public string? PublicUrl { get; set; }
}

/// <summary>
/// Emails waiting to be sent. Notifications are a courtesy: they are sent after the request that
/// caused them has answered, and a failure never undoes the action.
/// </summary>
public class NotificationQueue
{
    public const int Capacity = 2000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>False when the queue is full and the message was dropped.</summary>
    public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Sends the queued notifications, one at a time.</summary>
public class NotificationWorker(NotificationQueue queue, IEmailSender sender, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (EmailNotConfiguredException ex)
            {
                logger.LogDebug(ex, "A task notification was not sent: no SMTP server is configured");
            }
            catch (EmailSendException)
            {
                // Already logged by the sender. The task itself is unaffected.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending a task notification failed");
            }
        }
    }
}

/// <summary>Why someone is being told about a task.</summary>
public enum TaskNotificationReason
{
    Assigned,
    Forwarded,
    /// <summary>Completed by someone it was forwarded to, and sent back to whoever asked for it back.</summary>
    Returned,
    /// <summary>A copy of a task, given to someone by the person responsible for it.</summary>
    Distributed,
    /// <summary>Someone answered a task distributed to them; told to the distributor.</summary>
    Answered,
    /// <summary>The last person answered a distributed task; told to the distributor.</summary>
    AllAnswered,
    /// <summary>The distributor sent someone's answered copy back to them.</summary>
    SentBack,
    /// <summary>A message in the conversation of a distributed task.</summary>
    Message,
    /// <summary>The distributor closed someone's copy without waiting for the answer.</summary>
    Closed,
    /// <summary>A manager moved the due date of a task the person is responsible for.</summary>
    DueDateChanged,
    /// <summary>A manager moved a task the person is responsible for to another day or level.</summary>
    Rescheduled
}

/// <summary>Tells people by email that a task reached them, directly or through one of their groups.</summary>
public class TaskNotifier(
    TaskelDbContext db,
    NotificationQueue queue,
    IntegrationSettings settings,
    ILogger<TaskNotifier> logger)
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>
    /// Queues an email for whoever the task now waits on: its assignee, or every member of its group
    /// while nobody has taken it. The person who did it is never told, and neither is anyone who
    /// turned the notifications off. Never throws: a notification must not fail the action.
    /// </summary>
    public async Task TaskReceivedAsync(TaskItem task, Guid actorId, TaskNotificationReason reason, string? note = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!settings.Notifications.Enabled || !settings.Smtp.IsConfigured) return;
            if (task.IsCompleted) return;

            var (recipients, group) = await RecipientsAsync(task, reason, ct);
            if (recipients.Count == 0) return;

            var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);
            if (actor is null) return;

            foreach (var recipient in recipients.Where(u => u.Id != actorId && u.EmailNotifications))
            {
                var message = Compose(task, recipient, actor, group, reason, note, settings.Notifications.PublicUrl);
                if (!queue.TryEnqueue(message))
                {
                    logger.LogWarning("The notification queue is full; a task notification was dropped");
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Preparing the notifications for task {TaskId} failed", task.Id);
        }
    }

    /// <summary>
    /// Queues an email for one person about a task: the distributor when an answer arrives, a recipient when their
    /// copy is closed, either side of a conversation. Unlike <see cref="TaskReceivedAsync"/> it is sent for a
    /// completed task too. Never throws.
    /// </summary>
    public async Task TellAsync(TaskItem task, Guid recipientId, Guid actorId, TaskNotificationReason reason, string? note,
        CancellationToken ct = default)
    {
        try
        {
            if (!settings.Notifications.Enabled || !settings.Smtp.IsConfigured || recipientId == actorId) return;
            var recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == recipientId, ct);
            var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);
            if (recipient is null || actor is null || !recipient.EmailNotifications) return;
            if (!queue.TryEnqueue(Compose(task, recipient, actor, null, reason, note, settings.Notifications.PublicUrl)))
                logger.LogWarning("The notification queue is full; a task notification was dropped");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Preparing a notification for task {TaskId} failed", task.Id);
        }
    }

    /// <summary>
    /// Who the task now waits on: its assignee; every member of its group while nobody has taken it;
    /// or its creator when it was forwarded back to them. Nobody otherwise.
    /// </summary>
    private async Task<(List<User> Recipients, Group? Group)> RecipientsAsync(
        TaskItem task, TaskNotificationReason reason, CancellationToken ct)
    {
        if (task.AssigneeId is { } assigneeId)
            return (await db.Users.AsNoTracking().Where(u => u.Id == assigneeId).ToListAsync(ct), null);

        if (task.GroupId is { } groupId)
        {
            var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
            if (group is null) return ([], null);
            var members = await db.Users.AsNoTracking()
                .Where(u => db.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == u.Id))
                .ToListAsync(ct);
            return (members, group);
        }

        // Forwarded or sent back to the person who created it.
        return reason is TaskNotificationReason.Forwarded or TaskNotificationReason.Returned
            ? (await db.Users.AsNoTracking().Where(u => u.Id == task.UserId).ToListAsync(ct), null)
            : ([], null);
    }

    /// <summary>
    /// The email one person gets: a short text, and the same as a card in HTML, with a button that opens the task.
    /// Written in Romanian, like the interface.
    /// </summary>
    public static EmailMessage Compose(TaskItem task, User recipient, User actor, Group? group,
        TaskNotificationReason reason, string? note, string? publicUrl)
    {
        var actorName = NameOf(actor);
        var what = WhatHappened(group, reason);
        var noteLabel = reason switch
        {
            TaskNotificationReason.Returned => "Rezolvarea lui ",
            TaskNotificationReason.Answered or TaskNotificationReason.AllAnswered => "Răspunsul lui ",
            TaskNotificationReason.DueDateChanged => "Termenul, schimbat de ",
            TaskNotificationReason.Rescheduled => "Ce a schimbat ",
            _ => "Mesaj de la "
        } + actorName;
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var link = LinkTo(task, publicUrl);

        var body = new StringBuilder();
        body.Append("Bună, ").Append(NameOf(recipient)).Append(",\n\n");
        body.Append(actorName).Append(' ').Append(what).Append("\n\n");
        AppendFacts(body, task);
        if (text is not null) body.Append(noteLabel).Append(text.Contains('\n') ? ":\n" : ": ").Append(text).Append('\n');
        if (link is not null) body.Append("\nDeschide în Taskel: ").Append(link).Append('\n');
        body.Append("\n--\n").Append(Footer).Append('\n');

        var html = Card(task, recipient, actorName, what, reason, noteLabel, text, link);
        return new EmailMessage(recipient.Email, SubjectOf(task, group, reason), body.ToString(), html);
    }

    private const string Footer =
        "Primești acest email pentru că ai un cont în Taskel. Poți opri notificările din Taskel, la Integrări → Notificări pe email.";

    /// <summary>The address that opens the task in Taskel; null when Taskel's address is not set.</summary>
    public static string? LinkTo(TaskItem task, string? publicUrl)
    {
        var url = publicUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(url) ? null : $"{url}/?task={task.Id}";
    }

    /// <summary>The notification as a card: what happened, the task's title and facts, the message, a button.</summary>
    private static string Card(TaskItem task, User recipient, string actorName, string what, TaskNotificationReason reason,
        string noteLabel, string? note, string? link)
    {
        static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
        static string Lines(string text) => E(text).Replace("\n", "<br>");
        const string font = "font-family:Segoe UI,Arial,sans-serif;color:#1b1b1f";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var facts = new List<(string Label, string Value, bool Alert)>();
        if (task.DueDate is { } due)
        {
            var late = !task.IsCompleted && due <= today;
            facts.Add(task.StartDate is { } start && start != due
                ? ("Interval", $"{Date(start)} – {Date(due)}", late)
                : ("Termen", due == today ? $"azi, {Date(due)}" : Date(due), late));
        }
        var priority = task.Priority switch { TaskPriority.High => "Ridicată", TaskPriority.Low => "Scăzută", _ => "Medie" };
        facts.Add(("Prioritate", task.PriorityLevel is { } level ? $"{priority} · nivel {level}" : priority, task.Priority == TaskPriority.High));
        if (reason == TaskNotificationReason.Distributed) facts.Add(("Răspunsul tău", $"ajunge la {actorName}", false));
        if (task.Attachments.Count > 0)
            facts.Add(("Fișiere", string.Join(", ", task.Attachments.Where(a => !a.WithAnswer).Select(a => a.FileName).DefaultIfEmpty("—")), false));

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:24px 12px;background:#f4f5f7;").Append(font).Append("\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px;background:#ffffff;border:1px solid #e1e2e6;border-radius:10px\">")
            .Append("<tr><td style=\"padding:14px 22px;border-bottom:1px solid #ececef;font-size:15px;font-weight:600\">")
            .Append("<span style=\"display:inline-block;width:22px;height:22px;line-height:22px;margin-right:8px;border-radius:5px;background:#0b0b0c;color:#ffffff;text-align:center;font-size:13px\">&#10003;</span>Taskel</td></tr>")
            .Append("<tr><td style=\"padding:20px 22px 6px\">")
            .Append("<p style=\"margin:0 0 4px;font-size:13px;color:#55555c\">Bună, ").Append(E(NameOf(recipient))).Append(". ")
            .Append(E(actorName)).Append(' ').Append(E(what)).Append("</p>")
            .Append("<h1 style=\"margin:0 0 14px;font-size:20px;line-height:1.3\">").Append(E(task.Title)).Append("</h1>")
            .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 16px;font-size:14px\">");
        foreach (var (label, value, alert) in facts)
        {
            html.Append("<tr><td style=\"padding:3px 16px 3px 0;color:#6b6b73;white-space:nowrap\">").Append(E(label)).Append("</td>")
                .Append("<td style=\"padding:3px 0;font-weight:500;").Append(alert ? "color:#b42318" : "").Append("\">").Append(E(value)).Append("</td></tr>");
        }
        html.Append("</table>");
        if (note is not null)
        {
            html.Append("<div style=\"margin:0 0 18px;padding:12px 14px;border-radius:8px;background:#f4f5f7;font-size:14px;line-height:1.5\">")
                .Append("<div style=\"margin:0 0 4px;font-size:12.5px;color:#6b6b73\">").Append(E(noteLabel)).Append("</div>")
                .Append(Lines(note)).Append("</div>");
        }
        if (link is not null)
        {
            html.Append("<p style=\"margin:0 0 18px\"><a href=\"").Append(E(link))
                .Append("\" style=\"display:inline-block;padding:10px 18px;border-radius:6px;background:#0b0b0c;color:#ffffff;font-weight:600;font-size:14px;text-decoration:none\">Deschide în Taskel</a></p>");
        }
        html.Append("</td></tr><tr><td style=\"padding:12px 22px 16px;border-top:1px solid #ececef;color:#8a8a92;font-size:12px;line-height:1.5\">")
            .Append(E(Footer)).Append("</td></tr></table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static string SubjectOf(TaskItem task, Group? group, TaskNotificationReason reason)
    {
        switch (reason)
        {
            case TaskNotificationReason.Returned: return $"Task rezolvat, înapoi la tine: {task.Title}";
            case TaskNotificationReason.Answered: return $"Răspuns primit: {task.Title}";
            case TaskNotificationReason.AllAnswered: return $"Toți au răspuns: {task.Title}";
            case TaskNotificationReason.SentBack: return $"Task trimis înapoi: {task.Title}";
            case TaskNotificationReason.Message: return $"Mesaj nou: {task.Title}";
            case TaskNotificationReason.Closed: return $"Task închis: {task.Title}";
            case TaskNotificationReason.DueDateChanged: return $"Termen schimbat: {task.Title}";
            case TaskNotificationReason.Rescheduled: return $"Task replanificat: {task.Title}";
        }
        return group is not null ? $"Task pentru grupul {group.Name}: {task.Title}" : $"Task nou: {task.Title}";
    }

    private static string WhatHappened(Group? group, TaskNotificationReason reason)
    {
        switch (reason)
        {
            case TaskNotificationReason.Returned:
                return "a rezolvat un task pe care l-ai direcționat și ți l-a trimis înapoi. Îl poți închide sau duce mai departe.";
            case TaskNotificationReason.Answered:
                return "a răspuns la task-ul pe care i l-ai distribuit. Îl vezi în conversația task-ului tău.";
            case TaskNotificationReason.AllAnswered:
                return "a răspuns ultimul la task-ul pe care l-ai distribuit. Task-ul tău e din nou deschis: îl poți închide.";
            case TaskNotificationReason.SentBack:
                return "ți-a trimis înapoi task-ul la care ai răspuns. E din nou deschis.";
            case TaskNotificationReason.Message:
                return "ți-a scris în conversația unui task.";
            case TaskNotificationReason.Closed:
                return "a închis task-ul pe care ți-l distribuise. Nu mai e nevoie de răspunsul tău.";
            case TaskNotificationReason.DueDateChanged:
                return "ți-a schimbat termenul unui task de care răspunzi.";
            case TaskNotificationReason.Rescheduled:
                return "ți-a replanificat un task de care răspunzi.";
            case TaskNotificationReason.Distributed:
                return group is null
                    ? "ți-a distribuit un task în Taskel. Când îl finalizezi, răspunsul tău ajunge înapoi la această persoană."
                    : "a distribuit un task grupului " + group.Name
                      + ", din care faci parte. Îl vede tot grupul, până când unul dintre voi îl preia.";
        }
        var forwarded = reason == TaskNotificationReason.Forwarded;
        if (group is null) return forwarded ? "ți-a direcționat un task în Taskel." : "ți-a alocat un task în Taskel.";
        return (forwarded ? "a direcționat un task către grupul " : "a dat un task grupului ") + group.Name
               + ", din care faci parte. Îl vede tot grupul, până când unul dintre voi îl preia.";
    }

    /// <summary>The task's title, dates and priority, one per line.</summary>
    private static void AppendFacts(StringBuilder body, TaskItem task)
    {
        body.Append("Task: ").Append(task.Title).Append('\n');
        if (task.DueDate is { } due)
        {
            body.Append(task.StartDate is { } start && start != due
                ? $"Interval: {Date(start)} – {Date(due)}"
                : $"Termen: {Date(due)}").Append('\n');
        }
        if (task.Priority != TaskPriority.Medium)
            body.Append("Prioritate: ").Append(task.Priority == TaskPriority.High ? "ridicată" : "scăzută").Append('\n');
    }

    private static string NameOf(User user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

    private static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Romanian);
}
