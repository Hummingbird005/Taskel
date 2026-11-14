using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Taskel.Api.Attachments;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>The attachment size limit, for administrators.</summary>
[ApiController]
[Authorize]
[AdminOnly("Only administrators can change the attachment limit.")]
[Route("api/admin/settings/attachments")]
public class AttachmentSettingsController(AttachmentStore attachments, IOptions<AttachmentOptions> options) : ControllerBase
{
    /// <summary>The limit, with the server default it replaces.</summary>
    [HttpGet]
    public async Task<ActionResult<AttachmentSettingsResponse>> Get(CancellationToken ct) => Ok(await ResponseAsync(ct));

    [HttpPut]
    public async Task<ActionResult<AttachmentSettingsResponse>> Update(UpdateAttachmentSettingsRequest request, CancellationToken ct)
    {
        await attachments.SetMaxFileSizeMbAsync(request.MaxFileSizeMb, ct);
        return Ok(await ResponseAsync(ct));
    }

    private async Task<AttachmentSettingsResponse> ResponseAsync(CancellationToken ct) => new(
        await attachments.GetMaxFileSizeMbAsync(ct),
        Math.Clamp(options.Value.MaxFileSizeMb, 1, AttachmentOptions.MaxConfigurableMb),
        AttachmentOptions.MaxConfigurableMb);
}
