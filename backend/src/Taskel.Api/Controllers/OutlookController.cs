using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;

namespace Taskel.Api.Controllers;

/// <summary>
/// What the Outlook macro needs that the interface gets elsewhere, in a form VBA reads without a JSON
/// parser: one line per entry, fields separated by tabs.
/// </summary>
[ApiController]
[Authorize]
[Route("api/outlook")]
public class OutlookController(TaskelDbContext db) : ControllerBase
{
    /// <summary>
    /// Who an email can be given to: every person, then every group, as
    /// <c>person|group TAB value TAB name TAB detail</c>, where value is what
    /// <c>from-email/distribute</c> takes as a recipient. The caller's own line says so in its detail.
    /// </summary>
    [HttpGet("recipients")]
    public async Task<ContentResult> Recipients(CancellationToken ct)
    {
        var me = User.GetUserId();
        var people = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(ct);
        var groups = await db.Groups.AsNoTracking()
            .Select(g => new { g.Id, g.Name, Members = g.Members.Count })
            .ToListAsync(ct);

        var text = new StringBuilder();
        foreach (var person in people
                     .Select(p => new { p.Id, p.Email, Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Email : p.DisplayName })
                     .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Line(text, "person", person.Id.ToString(), person.Name, person.Id == me ? $"{person.Email} (eu)" : person.Email);
        }
        foreach (var group in groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Line(text, "group", $"group:{group.Id}", group.Name, group.Members == 1 ? "grup, 1 membru" : $"grup, {group.Members} membri");
        }
        return Content(text.ToString(), "text/plain", Encoding.UTF8);
    }

    private static void Line(StringBuilder text, string kind, string value, string name, string detail) =>
        text.Append(kind).Append('\t').Append(value).Append('\t').Append(Clean(name)).Append('\t').Append(Clean(detail)).Append('\n');

    /// <summary>Names are single-line already; a tab or line break would still break the format, so none gets through.</summary>
    private static string Clean(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
