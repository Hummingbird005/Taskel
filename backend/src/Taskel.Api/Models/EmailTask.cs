using System.ComponentModel.DataAnnotations;

namespace Taskel.Api.Models;

public enum EmailImportance
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>An email to turn into a task. Long values are accepted and shortened to fit the task.</summary>
public record CreateTaskFromEmailRequest(
    [StringLength(1000)] string? Subject,
    [StringLength(320)] string? Sender = null,
    /// <summary>When the email was received, in the sender's local time. Shown as given.</summary>
    DateTime? ReceivedAt = null,
    [StringLength(20000)] string? Body = null,
    [EnumDataType(typeof(EmailImportance))] EmailImportance Importance = EmailImportance.Normal,
    DateOnly? DueDate = null);
