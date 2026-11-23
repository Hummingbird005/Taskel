using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Taskel.Api.Models;

namespace Taskel.Api.Attachments;

public static class AttachmentDownload
{
    /// <summary>
    /// Sends a stored file back. As a download of unspecified type, whatever was uploaded, so a browser never opens
    /// an uploaded page or script as part of Taskel; or, with <paramref name="view"/>, a PDF or a picture to be shown
    /// (<see cref="PreviewType"/>).
    /// </summary>
    public static async Task<IActionResult> SendAsync(ControllerBase controller, AttachmentStore store, TaskAttachment attachment,
        ILogger logger, bool view, CancellationToken ct)
    {
        var content = store.Open(attachment.StoredName);
        if (content is null)
        {
            logger.LogError("The file of attachment {AttachmentId} is missing from disk", attachment.Id);
            return controller.Problem(statusCode: StatusCodes.Status404NotFound, title: "The file is no longer stored on the server.");
        }
        controller.Response.Headers.XContentTypeOptions = "nosniff";
        if (view) return await ViewAsync(controller, content, attachment, ct);

        controller.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return controller.File(content, "application/octet-stream", attachment.FileName);
    }

    /// <summary>
    /// Sends a PDF or a picture to be shown in the browser rather than saved, with the type its content proves.
    /// Any other file is refused with 415: it can still be downloaded.
    /// </summary>
    private static async Task<IActionResult> ViewAsync(ControllerBase controller, Stream content, TaskAttachment attachment, CancellationToken ct)
    {
        var type = await PreviewType.OfAsync(content, ct);
        if (type is null)
        {
            await content.DisposeAsync();
            return controller.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "This file cannot be shown in Taskel. Download it instead.");
        }

        var headers = controller.Response.Headers;
        // A picture needs nothing but itself. The browser's PDF viewer does not start inside a sandbox, so a PDF only
        // keeps the rule that no other site may frame it; it is shown by the browser, never run as a page of Taskel.
        headers.ContentSecurityPolicy = type == PreviewType.Pdf
            ? "frame-ancestors 'self'"
            : "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'; sandbox";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = attachment.FileName }.ToString();
        return controller.File(content, type, enableRangeProcessing: true);
    }
}
