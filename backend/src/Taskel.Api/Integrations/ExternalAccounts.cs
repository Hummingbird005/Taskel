using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Integrations;

/// <summary>A group a user belongs to in an identity provider, keyed by what identifies it there.</summary>
/// <param name="Key">Lower-case and unique across providers: a distinguished name, or <c>keycloak:</c> and a group path.</param>
public record ExternalGroup(string Key, string Name);

/// <summary>What Active Directory and Keycloak sign-ins have in common: roles and groups that follow the provider.</summary>
public class ExternalAccounts(TaskelDbContext db)
{
    /// <summary>
    /// A configured group or role both grants and withdraws its Taskel role. A role nothing speaks
    /// for (null) is left as it is, so one given by hand in Taskel survives.
    /// </summary>
    public static UserRole Role(UserRole current, bool? isManager, bool? isAdmin)
    {
        var role = current;
        if (isAdmin == false && role == UserRole.Admin) role = UserRole.User;
        if (isManager == false && role == UserRole.Manager) role = UserRole.User;
        if (isManager == true && role == UserRole.User) role = UserRole.Manager;
        if (isAdmin == true) role = UserRole.Admin;
        return role;
    }

    /// <summary>
    /// Makes the user's memberships of <paramref name="kind"/> groups match the provider: groups seen
    /// for the first time are created, and the user leaves the ones they no longer belong to.
    /// </summary>
    public async Task SyncGroupsAsync(User user, GroupKind kind, IReadOnlyList<ExternalGroup> groups, CancellationToken ct)
    {
        var keys = groups.Select(g => g.Key).Distinct().ToList();
        var known = await db.Groups
            .Where(g => g.Kind == kind && g.DirectoryDn != null && keys.Contains(g.DirectoryDn))
            .ToListAsync(ct);

        var current = new HashSet<Guid>();
        foreach (var external in groups.DistinctBy(g => g.Key))
        {
            var group = known.FirstOrDefault(g => g.DirectoryDn == external.Key);
            if (group is null)
            {
                group = new Group { Kind = kind, DirectoryDn = external.Key, Name = external.Name };
                db.Groups.Add(group);
                known.Add(group);
            }
            else
            {
                group.Name = external.Name;
            }
            current.Add(group.Id);
        }

        var memberships = await db.GroupMembers
            .Where(m => m.UserId == user.Id && db.Groups.Any(g => g.Id == m.GroupId && g.Kind == kind))
            .ToListAsync(ct);
        db.GroupMembers.RemoveRange(memberships.Where(m => !current.Contains(m.GroupId)));
        var already = memberships.Select(m => m.GroupId).ToHashSet();
        db.GroupMembers.AddRange(current.Where(id => !already.Contains(id))
            .Select(id => new GroupMember { GroupId = id, UserId = user.Id }));
        await db.SaveChangesAsync(ct);
    }
}
