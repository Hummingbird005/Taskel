using System.Globalization;
using System.Text.RegularExpressions;
using Taskel.Api.Models;

namespace Taskel.Api.Services;

/// <summary>Builds a task from an email, shortening the subject and body to the task's limits.</summary>
public static partial class EmailTaskFactory
{
    public const string EmailTag = "email";
    public const string NoSubjectTitle = "(no subject)";
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;

    public static TaskItem Create(Guid userId, CreateTaskFromEmailRequest email)
    {
        var title = Whitespace().Replace(email.Subject ?? string.Empty, " ").Trim();

        var header = new List<string>();
        if (!string.IsNullOrWhiteSpace(email.Sender))
            header.Add($"From: {Whitespace().Replace(email.Sender, " ").Trim()}");
        if (email.ReceivedAt is { } received)
            header.Add($"Received: {received.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

        var body = (email.Body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        body = BlankLines().Replace(body, "\n\n").Trim();

        var description = string.Join("\n\n",
            new[] { string.Join("\n", header), body }.Where(part => part.Length > 0));

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
            Tags = [EmailTag]
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)].TrimEnd() + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex BlankLines();
}
