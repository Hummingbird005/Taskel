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
        await db.SaveChangesAsync(ct);
        return user;
    }
}
