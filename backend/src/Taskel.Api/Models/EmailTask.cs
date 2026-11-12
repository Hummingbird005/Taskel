using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

public enum EmailImportance
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>
/// An email to turn into a task. Emails come from outside, so the text is cleaned and shortened to fit
/// the task rather than rejected: only the overall lengths are validated here.
/// </summary>
public record CreateTaskFromEmailRequest(
    [StringLength(1000)] string? Subject,
    [StringLength(320)] string? Sender = null,
    /// <summary>When the email was received, in the sender's local time. Shown as given.</summary>
    DateTime? ReceivedAt = null,
    [StringLength(20000)] string? Body = null,
    [EnumDataType(typeof(EmailImportance))] EmailImportance Importance = EmailImportance.Normal,
    [DueDateRange] DateOnly? DueDate = null,
    /// <summary>The sender's plain email address; the reply on completion is sent here.</summary>
    [StringLength(320)] string? SenderEmail = null,
    /// <summary>The email's Message-ID header, e.g. <c>&lt;abc@mail.example.com&gt;</c>.</summary>
    [StringLength(998)] string? MessageId = null);
