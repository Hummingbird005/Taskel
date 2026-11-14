using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Email;
using Taskel.Api.Integrations;

namespace Taskel.Api.Controllers;

/// <summary>The mail server Taskel sends through, and the notifications sent with it.</summary>
[Route(RoutePrefix + "email")]
public class EmailSettingsController(
    TaskelDbContext db,
    IntegrationSettings settings,
    IEmailSender email) : IntegrationSettingsControllerBase(db, settings)
{
    [HttpGet]
    public async Task<ActionResult<EmailSettingsResponse>> GetEmail(CancellationToken ct) => Ok(await EmailResponseAsync(ct));

    [HttpPut]
    public async Task<ActionResult<EmailSettingsResponse>> SaveEmail(EmailSettingsRequest request, CancellationToken ct)
    {
        await Settings.SaveEmailAsync(request.ToSettings(), request.Password, User.GetUserId(), ct);
        return Ok(await EmailResponseAsync(ct));
    }

    [HttpDelete]
    public async Task<ActionResult<EmailSettingsResponse>> ResetEmail(CancellationToken ct)
    {
        await Settings.ResetAsync(IntegrationKinds.Email, ct);
        return Ok(await EmailResponseAsync(ct));
    }

    /// <summary>Sends a message with the settings in the form (not yet saved) to the administrator's own address.</summary>
    [HttpPost("test")]
    [EnableRateLimiting(AuthController.LoginRateLimit)]
    public async Task<ActionResult<EmailTestResponse>> TestEmail(EmailTestRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var to = await Db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (to is null) return Unauthorized();

        var candidate = request.Settings.ToSettings().Smtp;
        candidate.Password = Settings.ResolveSecret(IntegrationKinds.Email, request.Settings.Password);
        if (!candidate.IsConfigured)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter the SMTP server and the sender address.");

        try
        {
            await email.SendTestAsync(new EmailMessage(to, "Taskel: mesaj de test",
                "Acesta este un mesaj de test trimis din Taskel, din pagina Integrări a administratorului.\n\n" +
                "Dacă l-ai primit, setările serverului de email funcționează."), candidate, ct);
        }
        catch (EmailSendException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The SMTP server did not accept the message.",
                detail: Reason(ex));
        }
        return Ok(new EmailTestResponse(to));
    }

    private async Task<EmailSettingsResponse> EmailResponseAsync(CancellationToken ct)
    {
        var smtp = Settings.Smtp;
        var notifications = Settings.Notifications;
        return new EmailSettingsResponse(smtp.Host, smtp.Port, smtp.Username, smtp.FromAddress, smtp.FromName, smtp.RequireTls,
            notifications.Enabled, notifications.PublicUrl, await StatusAsync(IntegrationKinds.Email, smtp.IsConfigured, ct));
    }
}
