using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>
/// Groups a task can be given to. Everyone can see the list and use it; application groups are
/// managed by administrators (all of them) and managers (the ones they created, with people from
/// their own department). Active Directory groups are read-only here.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GroupsController(TaskelDbContext db, CurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GroupListItem>>> GetAll(CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();

        var groups = await db.Groups.AsNoTracking()
            .Select(g => new { Group = g, MemberCount = g.Members.Count })
            .ToListAsync(ct);
        return Ok(groups
            .Select(g => new GroupListItem(g.Group.Id, g.Group.Name, g.Group.Kind, g.MemberCount, CanManage(g.Group, me.Value)))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Kind));
    }

    /// <summary>A group with its members. For managers and administrators.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GroupDetail>> Get(Guid id, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        if (me.Value.Role == UserRole.User) return ManagersOnly();

        var group = await WithMembers().AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return group is null ? NotFound() : Ok(ToDetail(group, me.Value));
    }

    /// <summary>The people the caller may put in a group: everyone for an administrator, their department for a manager.</summary>
    [HttpGet("candidates")]
    public async Task<ActionResult<IEnumerable<UserSummary>>> GetCandidates(CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        if (me.Value.Role == UserRole.User) return ManagersOnly();

        var people = await AllowedMembers(me.Value).AsNoTracking().ToListAsync(ct);
        return Ok(people.Select(UserSummary.From).OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase));
    }

    [HttpPost]
    public async Task<ActionResult<GroupDetail>> Create(SaveGroupRequest request, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();
        if (me.Value.Role == UserRole.User) return ManagersOnly();

        var name = request.Name.Trim();
        var members = await ValidateAsync(me.Value, name, request.MemberIds, exceptGroupId: null, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var group = new Group
        {
            Name = name,
            Kind = GroupKind.App,
            CreatedById = me.Value.User.Id,
            Members = members.Select(id => new GroupMember { UserId = id }).ToList()
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(await WithMembers().AsNoTracking().FirstAsync(g => g.Id == group.Id, ct), me.Value));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GroupDetail>> Update(Guid id, SaveGroupRequest request, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();

        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();
        if (!CanManage(group, me.Value)) return CannotManage(group);

        var name = request.Name.Trim();
        var members = await ValidateAsync(me.Value, name, request.MemberIds, group.Id, ct, keep: group.Members.Select(m => m.UserId));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        group.Name = name;
        group.Members.RemoveAll(m => !members.Contains(m.UserId));
        var existing = group.Members.Select(m => m.UserId).ToHashSet();
        group.Members.AddRange(members.Where(memberId => !existing.Contains(memberId))
            .Select(memberId => new GroupMember { GroupId = group.Id, UserId = memberId }));
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(await WithMembers().AsNoTracking().FirstAsync(g => g.Id == group.Id, ct), me.Value));
    }

    /// <summary>Deletes an application group. Its unclaimed tasks return to the people who created them.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(User, ct);
        if (me is null) return Unauthorized();

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();
        if (!CanManage(group, me.Value)) return CannotManage(group);

        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private IQueryable<Group> WithMembers() => db.Groups.Include(g => g.Members).ThenInclude(m => m.User);

    private static GroupDetail ToDetail(Group group, (User User, UserRole Role) me) => new(
        group.Id, group.Name, group.Kind, CanManage(group, me),
        group.Members
            .Where(m => m.User is not null)
            .Select(m => UserSummary.From(m.User!))
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    private static bool CanManage(Group group, (User User, UserRole Role) me) =>
        group.Kind == GroupKind.App
        && (me.Role == UserRole.Admin || (me.Role == UserRole.Manager && group.CreatedById == me.User.Id));

    private IQueryable<User> AllowedMembers((User User, UserRole Role) me)
    {
        if (me.Role == UserRole.Admin) return db.Users;
        var department = me.User.Department;
        return department is null
            ? db.Users.Where(u => u.Id == me.User.Id)
            : db.Users.Where(u => u.Department != null && u.Department.ToLower() == department.ToLower());
    }

    /// <summary>
    /// Checks a group's name and members and returns the distinct member ids. A manager may only add
    /// people from their department, but members already in the group (<paramref name="keep"/>) may stay.
    /// </summary>
    private async Task<HashSet<Guid>> ValidateAsync((User User, UserRole Role) me, string name, List<Guid> memberIds,
        Guid? exceptGroupId, CancellationToken ct, IEnumerable<Guid>? keep = null)
    {
        var ids = memberIds.ToHashSet();
        if (await db.Groups.AnyAsync(g => g.Kind == GroupKind.App && g.Id != exceptGroupId && g.Name.ToLower() == name.ToLower(), ct))
            ModelState.AddModelError(nameof(SaveGroupRequest.Name), "There is already a group with this name.");

        var kept = keep?.ToHashSet() ?? [];
        var toCheck = ids.Where(memberId => !kept.Contains(memberId)).ToList();
        var allowed = await AllowedMembers(me).Where(u => toCheck.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        if (allowed.Count != toCheck.Count)
        {
            ModelState.AddModelError(nameof(SaveGroupRequest.MemberIds), me.Role == UserRole.Admin
                ? "Some of the members do not exist."
                : "You can only add people from your own department.");
        }
        return ids;
    }

    private ObjectResult ManagersOnly() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only managers and administrators can manage groups.");

    private ObjectResult CannotManage(Group group) =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: group.Kind == GroupKind.Directory
            ? "Active Directory groups are managed in Active Directory."
            : "You can only manage the groups you created.");
}
