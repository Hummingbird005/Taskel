using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
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
    Forwarded
}

/// <summary>Tells people by email that a task reached them, directly or through one of their groups.</summary>
public class TaskNotifier(
    TaskelDbContext db,
    NotificationQueue queue,
    IOptions<NotificationOptions> options,
    IOptions<SmtpOptions> smtp,
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
            if (!options.Value.Enabled || !smtp.Value.IsConfigured) return;
            if (task.IsCompleted) return;

            List<User> recipients;
            Group? group = null;
            if (task.AssigneeId is { } assigneeId)
            {
                recipients = await db.Users.AsNoTracking().Where(u => u.Id == assigneeId).ToListAsync(ct);
            }
            else if (task.GroupId is { } groupId)
            {
                group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
                if (group is null) return;
                recipients = await db.Users.AsNoTracking()
                    .Where(u => db.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == u.Id))
                    .ToListAsync(ct);
            }
            else if (reason == TaskNotificationReason.Forwarded)
            {
                // Forwarded back to the person who created it.
                recipients = await db.Users.AsNoTracking().Where(u => u.Id == task.UserId).ToListAsync(ct);
            }
            else
            {
                return;
            }

            var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);
            if (actor is null) return;

            foreach (var recipient in recipients.Where(u => u.Id != actorId && u.EmailNotifications))
            {
                var message = Compose(task, recipient, actor, group, reason, note, options.Value.PublicUrl);
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

    /// <summary>The email one person gets. Written in Romanian, like the interface.</summary>
    public static EmailMessage Compose(TaskItem task, User recipient, User actor, Group? group,
        TaskNotificationReason reason, string? note, string? publicUrl)
    {
        var actorName = NameOf(actor);
        var body = new StringBuilder();
        body.Append("Bună, ").Append(NameOf(recipient)).Append(",\n\n");
        body.Append(actorName).Append(' ');
        if (group is not null)
        {
            body.Append(reason == TaskNotificationReason.Forwarded ? "a direcționat un task către grupul " : "a dat un task grupului ")
                .Append(group.Name)
                .Append(", din care faci parte. Îl vede tot grupul, până când unul dintre voi îl preia.");
        }
        else
        {
            body.Append(reason == TaskNotificationReason.Forwarded ? "ți-a direcționat un task în Taskel." : "ți-a alocat un task în Taskel.");
        }
        body.Append("\n\n");

        body.Append("Task: ").Append(task.Title).Append('\n');
        if (task.DueDate is { } due)
        {
            body.Append(task.StartDate is { } start && start != due
                ? $"Interval: {Date(start)} – {Date(due)}"
                : $"Termen: {Date(due)}").Append('\n');
        }
        if (task.Priority != TaskPriority.Medium)
            body.Append("Prioritate: ").Append(task.Priority == TaskPriority.High ? "ridicată" : "scăzută").Append('\n');
        if (!string.IsNullOrWhiteSpace(note))
            body.Append("Mesaj de la ").Append(actorName).Append(": ").Append(note.Trim()).Append('\n');

        var url = publicUrl?.Trim();
        if (!string.IsNullOrEmpty(url)) body.Append("\nDeschide Taskel: ").Append(url).Append('\n');

        body.Append("\n--\nPrimești acest email pentru că ai un cont în Taskel. ")
            .Append("Poți opri notificările din Taskel, la Integrări → Notificări pe email.\n");

        var subject = group is not null ? $"Task pentru grupul {group.Name}: {task.Title}" : $"Task nou: {task.Title}";
        return new EmailMessage(recipient.Email, subject, body.ToString());
    }

    private static string NameOf(User user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

    private static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Romanian);
}
