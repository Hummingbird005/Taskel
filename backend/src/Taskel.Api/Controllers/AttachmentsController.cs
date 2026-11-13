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
        var access = TasksController.AccessOf(task, userId, await tasks.GetGroupIdsAsync(userId, ct));
        if (access is not (TaskAccess.Creator or TaskAccess.Assignee))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the people who can work on this task can attach files to it.");
        }
        if (task.Attachments.Count >= store.MaxFilesPerTask)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"A task can have at most {store.MaxFilesPerTask} files.");
        }

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
        {
            return Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Send the file as multipart/form-data, in a part named \"file\".");
        }

        var limit = await store.GetMaxFileSizeBytesAsync(ct);
        // The server's general limit is for small JSON bodies; this request may be as large as a file.
        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limit + EnvelopeAllowance;
        if (Request.ContentLength > limit + EnvelopeAllowance) return TooLarge(limit);

        string storedName;
        long size;
        string fileName;
        string contentType;
        try
        {
            var reader = new MultipartReader(boundary, Request.Body);
            MultipartSection? section;
            ContentDispositionHeaderValue? disposition = null;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out disposition)
                    && disposition.IsFileDisposition()
                    && HeaderUtilities.RemoveQuotes(disposition.Name).Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (section is null || disposition is null)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "The request has no file. Send it in a part named \"file\".");
            }

            fileName = AttachmentStore.CleanFileName(
                HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value);
            contentType = MediaTypeHeaderValue.TryParse(section.ContentType, out var partType) && partType.MediaType.Length <= 100
                ? partType.MediaType.Value!
                : "application/octet-stream";
            (storedName, size) = await store.SaveAsync(section.Body, limit, ct);
        }
        catch (FileTooLargeException)
        {
            return TooLarge(limit);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(limit);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            if (ex is IOException) logger.LogWarning(ex, "An upload to task {TaskId} was cut short", taskId);
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The upload was incomplete or malformed.");
        }

        if (size == 0)
        {
            store.Delete(storedName);
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The file is empty.");
        }

        var attachment = new TaskAttachment
        {
            TaskId = task.Id,
            FileName = fileName,
            ContentType = contentType,
            Size = size,
            StoredName = storedName,
            UploadedById = userId
        };
        try
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            store.Delete(storedName);
            throw;
        }
        await db.Entry(attachment).Reference(a => a.UploadedBy).LoadAsync(ct);
        return CreatedAtAction(nameof(Download), new { taskId, id = attachment.Id }, AttachmentResponse.From(attachment));
    }

    /// <summary>
    /// Sends a file back. Always as a download of unspecified type, whatever was uploaded, so a
    /// browser never opens an uploaded page or script as part of Taskel.
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/attachments/{id:guid}")]
    public async Task<IActionResult> Download(Guid taskId, Guid id, CancellationToken ct)
    {
        var task = await tasks.GetAsync(User.GetUserId(), taskId, ct);
        var attachment = task?.Attachments.FirstOrDefault(a => a.Id == id);
        if (attachment is null) return NotFound();

        var content = store.Open(attachment.StoredName);
        if (content is null)
        {
            logger.LogError("The file of attachment {AttachmentId} is missing from disk", id);
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "The file is no longer stored on the server.");
        }
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(content, "application/octet-stream", attachment.FileName);
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
        store.Delete(attachment.StoredName);
        return NoContent();
    }

    private ObjectResult TooLarge(long limit) =>
        Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
            title: $"The file is too large. The limit is {limit / (1024 * 1024)} MB.");
}
