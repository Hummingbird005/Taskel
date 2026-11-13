using Microsoft.AspNetCore.Mvc;
using Taskel.Api.Models;

namespace Taskel.Api.Attachments;

public static class AttachmentDownload
{
    /// <summary>
    /// Sends a stored file back. Always as a download of unspecified type, whatever was uploaded,
    /// so a browser never opens an uploaded page or script as part of Taskel.
    /// </summary>
    public static IActionResult Send(ControllerBase controller, AttachmentStore store, TaskAttachment attachment, ILogger logger)
    {
        var content = store.Open(attachment.StoredName);
        if (content is null)
        {
            logger.LogError("The file of attachment {AttachmentId} is missing from disk", attachment.Id);
            return controller.Problem(statusCode: StatusCodes.Status404NotFound, title: "The file is no longer stored on the server.");
        }
        controller.Response.Headers.XContentTypeOptions = "nosniff";
        controller.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return controller.File(content, "application/octet-stream", attachment.FileName);
    }
}
