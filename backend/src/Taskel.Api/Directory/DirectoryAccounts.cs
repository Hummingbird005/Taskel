using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Directory;

/// <summary>Keeps Taskel accounts in step with the directory accounts that sign in.</summary>
public class DirectoryAccounts(TaskelDbContext db)
{
    /// <summary>
    /// Returns the Taskel account for a directory user, creating it on first sign-in.
    /// A local account with the same email is taken over by the directory account: it keeps its
    /// tasks, and its local password stops working. Returns null when the email belongs to a
    /// different directory account.
    /// </summary>
    public async Task<User?> GetOrCreateAsync(DirectoryUser directoryUser, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.DirectoryId == directoryUser.DirectoryId, ct);
        var sameEmail = await db.Users.FirstOrDefaultAsync(u => u.Email == directoryUser.Email, ct);

        if (user is null)
        {
            if (sameEmail is { DirectoryId: not null }) return null;

            user = sameEmail ?? new User { Email = directoryUser.Email };
            user.DirectoryId = directoryUser.DirectoryId;
            user.PasswordHash = string.Empty;
            if (sameEmail is null) db.Users.Add(user);
        }
        else if (sameEmail is null)
        {
            // The address changed in the directory and is free here: follow it.
            user.Email = directoryUser.Email;
        }

        user.DisplayName = directoryUser.DisplayName;
        // The directory decides the department when it has one, and the role when a group is configured.
        if (directoryUser.Department is not null) user.Department = directoryUser.Department;
        user.Role = RoleFromDirectory(user.Role, directoryUser);
        await db.SaveChangesAsync(ct);
        if (directoryUser.Groups is not null) await SyncGroupsAsync(user, directoryUser.Groups, ct);
        return user;
    }

    /// <summary>
    /// Makes the user's directory-group memberships in Taskel match the directory: groups seen for
    /// the first time are created, and the user leaves the ones they no longer belong to.
    /// </summary>
    private async Task SyncGroupsAsync(User user, IReadOnlyList<DirectoryGroup> directoryGroups, CancellationToken ct)
    {
        var dns = directoryGroups.Select(g => g.Dn.ToLowerInvariant()).ToList();
        var known = await db.Groups
            .Where(g => g.Kind == GroupKind.Directory && g.DirectoryDn != null && dns.Contains(g.DirectoryDn))
            .ToListAsync(ct);

        var current = new HashSet<Guid>();
        foreach (var directoryGroup in directoryGroups)
        {
            var dn = directoryGroup.Dn.ToLowerInvariant();
            var group = known.FirstOrDefault(g => g.DirectoryDn == dn);
            if (group is null)
            {
                group = new Group { Kind = GroupKind.Directory, DirectoryDn = dn, Name = directoryGroup.Name };
                db.Groups.Add(group);
                known.Add(group);
            }
            else
            {
                group.Name = directoryGroup.Name;
            }
            current.Add(group.Id);
        }

        var memberships = await db.GroupMembers
            .Where(m => m.UserId == user.Id && db.Groups.Any(g => g.Id == m.GroupId && g.Kind == GroupKind.Directory))
            .ToListAsync(ct);
        db.GroupMembers.RemoveRange(memberships.Where(m => !current.Contains(m.GroupId)));
        var already = memberships.Select(m => m.GroupId).ToHashSet();
        db.GroupMembers.AddRange(current.Where(id => !already.Contains(id))
            .Select(id => new GroupMember { GroupId = id, UserId = user.Id }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A configured group both grants and withdraws its role. A role no group speaks for is left
    /// as it is, so one given by hand in Taskel survives.
    /// </summary>
    private static UserRole RoleFromDirectory(UserRole current, DirectoryUser directoryUser)
    {
        var role = current;
        if (directoryUser.IsAdmin == false && role == UserRole.Admin) role = UserRole.User;
        if (directoryUser.IsManager == false && role == UserRole.Manager) role = UserRole.User;
        if (directoryUser.IsManager == true && role == UserRole.User) role = UserRole.Manager;
        if (directoryUser.IsAdmin == true) role = UserRole.Admin;
        return role;
    }
}
