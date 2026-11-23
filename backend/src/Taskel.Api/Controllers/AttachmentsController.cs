using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Taskel.Api.Attachments;
using Taskel.Api.Data;
using Taskel.Api.Models;
using Taskel.Api.Services;

namespace Taskel.Api.Controllers;

/// <summary>
/// Files attached to tasks. Whoever can see a task can download its files; adding and removing
/// them is for the people who can work on it (its creator and its assignee).
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class AttachmentsController(ITaskRepository tasks, TaskelDbContext db, AttachmentStore store, ILogger<AttachmentsController> logger)
    : ControllerBase
{
    /// <summary>Room for the multipart boundaries and headers around the file itself.</summary>
    private const long EnvelopeAllowance = 64 * 1024;

    [HttpGet("attachments/limits")]
    public async Task<ActionResult<AttachmentLimitsResponse>> GetLimits(CancellationToken ct)
    {
        var megabytes = await store.GetMaxFileSizeMbAsync(ct);
        return Ok(new AttachmentLimitsResponse(megabytes, megabytes * 1024L * 1024L, store.MaxFilesPerTask));
    }

    /// <summary>
    /// Attaches one file to a task. The body is <c>multipart/form-data</c> with the file in a part
    /// named <c>file</c>. It is written to disk as it arrives, never held in memory.
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/attachments")]
    [StreamedUpload]
    public async Task<ActionResult<AttachmentResponse>> Upload(Guid taskId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await tasks.GetAsync(userId, taskId, ct);
        if (task is null) return NotFound();
        if (await RefuseUploadAsync(task, userId, ct) is { } refusal) return refusal;
        if (MultipartBoundary() is not { } boundary)
        {
            return Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Send the file as multipart/form-data, in a part named \"file\".");
        }

        var limit = await store.GetMaxFileSizeBytesAsync(ct);
        // The server's general limit is for small JSON bodies; this request may be as large as a file.
        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limit + EnvelopeAllowance;
        if (Request.ContentLength > limit + EnvelopeAllowance) return TooLarge(limit);

        var (file, failure) = await ReceiveAsync(taskId, boundary, limit, ct);
        if (file is null) return failure!;

        var attachment = new TaskAttachment
        {
            TaskId = task.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Size,
            StoredName = file.StoredName,
            UploadedById = userId
        };
        try
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            store.Delete(file.StoredName);
            throw;
        }
        await db.Entry(attachment).Reference(a => a.UploadedBy).LoadAsync(ct);
        return CreatedAtAction(nameof(Download), new { taskId, id = attachment.Id }, AttachmentResponse.From(attachment));
    }

    /// <summary>A file written to disk, not yet recorded on the task.</summary>
    private sealed record ReceivedFile(string StoredName, long Size, string FileName, string ContentType);

    /// <summary>Why the caller may not attach a file to this task now; null when they may.</summary>
    private async Task<ActionResult?> RefuseUploadAsync(TaskItem task, Guid userId, CancellationToken ct)
    {
        var access = TasksController.AccessOf(task, userId, await tasks.GetGroupIdsAsync(userId, ct));
        if (access is not (TaskAccess.Creator or TaskAccess.Assignee))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the people who can work on this task can attach files to it.");
        }
        return task.Attachments.Count >= store.MaxFilesPerTask
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: $"A task can have at most {store.MaxFilesPerTask} files.")
            : null;
    }

    /// <summary>The multipart boundary of the request, or null when the body is not multipart/form-data.</summary>
    private string? MultipartBoundary() =>
        MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType)
        && mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
        && HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is { Length: > 0 and <= 200 } boundary
            ? boundary
            : null;

    /// <summary>Reads the part named "file" and writes it to disk; on failure, nothing is left behind.</summary>
    private async Task<(ReceivedFile? File, ActionResult? Failure)> ReceiveAsync(
        Guid taskId, string boundary, long limit, CancellationToken ct)
    {
        try
        {
            var (section, disposition) = await FileSectionAsync(new MultipartReader(boundary, Request.Body), ct);
            if (section is null || disposition is null)
            {
                return (null, Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "The request has no file. Send it in a part named \"file\"."));
            }

            var fileName = AttachmentStore.CleanFileName(
                HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value);
            var contentType = MediaTypeHeaderValue.TryParse(section.ContentType, out var partType) && partType.MediaType.Length <= 100
                ? partType.MediaType.Value!
                : "application/octet-stream";
            var (storedName, size) = await store.SaveAsync(section.Body, limit, ct);
            if (size == 0)
            {
                store.Delete(storedName);
                return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "The file is empty."));
            }
            return (new ReceivedFile(storedName, size, fileName, contentType), null);
        }
        catch (FileTooLargeException)
        {
            return (null, TooLarge(limit));
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLarge(limit));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            if (ex is IOException) logger.LogWarning(ex, "An upload to task {TaskId} was cut short", taskId);
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "The upload was incomplete or malformed."));
        }
    }

    /// <summary>Skips to the part named "file"; nulls when there is none.</summary>
    private static async Task<(MultipartSection? Section, ContentDispositionHeaderValue? Disposition)> FileSectionAsync(
        MultipartReader reader, CancellationToken ct)
    {
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                && disposition.IsFileDisposition()
                && HeaderUtilities.RemoveQuotes(disposition.Name).Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                return (section, disposition);
            }
        }
        return (null, null);
    }

    /// <summary>Downloads a file of a task the caller can see, or, with <c>view=true</c>, shows a PDF or a picture.</summary>
    [HttpGet("tasks/{taskId:guid}/attachments/{id:guid}")]
    public async Task<IActionResult> Download(Guid taskId, Guid id, [FromQuery] bool view, CancellationToken ct)
    {
        var task = await tasks.GetAsync(User.GetUserId(), taskId, ct);
        var attachment = task?.Attachments.FirstOrDefault(a => a.Id == id);
        if (attachment is null) return NotFound();

        return await AttachmentDownload.SendAsync(this, store, attachment, logger, view, ct);
    }

    [HttpDelete("tasks/{taskId:guid}/attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var task = await tasks.GetAsync(userId, taskId, ct);
        var attachment = task?.Attachments.FirstOrDefault(a => a.Id == id);
        if (task is null || attachment is null) return NotFound();
        var access = TasksController.AccessOf(task, userId, await tasks.GetGroupIdsAsync(userId, ct));
        if (access is not (TaskAccess.Creator or TaskAccess.Assignee))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the people who can work on this task can remove its files.");
        }

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        // Distributed copies share their files with the task they came from: a file goes when nobody lists it.
        if (!await db.Attachments.AnyAsync(a => a.StoredName == attachment.StoredName, ct)) store.Delete(attachment.StoredName);
        return NoContent();
    }

    private ObjectResult TooLarge(long limit) =>
        Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
            title: $"The file is too large. The limit is {limit / (1024 * 1024)} MB.");
}
