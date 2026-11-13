using System.ComponentModel.DataAnnotations;
using Taskel.Api.Attachments;

namespace Taskel.Api.Models;

/// <summary>A file attached to a task. The content is on disk; this is what is known about it.</summary>
public class TaskAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    /// <summary>The name the file was uploaded with, cleaned. Shown to people; never used as a path.</summary>
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    /// <summary>The generated name of the file on disk.</summary>
    public string StoredName { get; set; } = string.Empty;
    public Guid? UploadedById { get; set; }
    public User? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A value an administrator set from the interface.</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public record AttachmentResponse(Guid Id, string FileName, long Size, string ContentType, UserSummary? UploadedBy, DateTime UploadedAt)
{
    public static AttachmentResponse From(TaskAttachment attachment) => new(
        attachment.Id, attachment.FileName, attachment.Size, attachment.ContentType,
        attachment.UploadedBy is null ? null : UserSummary.From(attachment.UploadedBy), attachment.UploadedAt);
}

/// <summary>What a client needs to know before offering an upload.</summary>
public record AttachmentLimitsResponse(int MaxFileSizeMb, long MaxFileSizeBytes, int MaxFilesPerTask);

/// <param name="MaxFileSizeMb">Largest file accepted, in megabytes.</param>
/// <param name="DefaultMaxFileSizeMb">The server's configured default, used until an administrator sets a value.</param>
public record AttachmentSettingsResponse(int MaxFileSizeMb, int DefaultMaxFileSizeMb, int MaxConfigurableMb);

public record UpdateAttachmentSettingsRequest([Range(1, AttachmentOptions.MaxConfigurableMb)] int MaxFileSizeMb);
