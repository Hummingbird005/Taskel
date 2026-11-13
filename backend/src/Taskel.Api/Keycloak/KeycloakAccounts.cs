using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taskel.Api.Data;
using Taskel.Api.Integrations;
using Taskel.Api.Models;

namespace Taskel.Api.Keycloak;

/// <summary>Why a Keycloak account could not be used in Taskel. The value travels to the sign-in screen.</summary>
public enum KeycloakRefusal
{
    /// <summary>Keycloak gave no usable email address.</summary>
    NoEmail,
    /// <summary>The address belongs to an Active Directory account or another Keycloak account.</summary>
    EmailTaken,
    /// <summary>The address belongs to a local account, and Keycloak has not verified it.</summary>
    EmailUnverified,
    /// <summary>The user lacks the role required to use Taskel.</summary>
    RoleMissing
}

/// <param name="Created">True when the Taskel account was made just now.</param>
public record KeycloakLink(User? User, KeycloakRefusal? Refusal, bool Created = false);

/// <summary>Keeps Taskel accounts in step with the Keycloak accounts that sign in or are imported.</summary>
public class KeycloakAccounts(TaskelDbContext db, ExternalAccounts external)
{
    /// <summary>
    /// Returns the Taskel account for a Keycloak user at sign-in, creating or linking it, with the
    /// name, department, role and groups Keycloak gives.
    /// </summary>
    public async Task<KeycloakLink> SignInAsync(KeycloakUser account, KeycloakOptions options, CancellationToken ct = default)
    {
        var roles = account.Roles ?? new HashSet<string>();
        bool? HasRole(string? role) => string.IsNullOrWhiteSpace(role) ? null : roles.Contains(role.Trim());

        if (HasRole(options.RequiredRole) == false) return new KeycloakLink(null, KeycloakRefusal.RoleMissing);

        var link = await LinkAsync(account, ct);
        if (link.User is not { } user) return link;

        Update(user, account);
        user.Role = ExternalAccounts.Role(user.Role, HasRole(options.ManagerRole), HasRole(options.AdminRole));
        await db.SaveChangesAsync(ct);
        if (account.Groups is not null)
        {
            await external.SyncGroupsAsync(user, GroupKind.Keycloak,
                account.Groups.Select(KeycloakClaims.Group).ToList(), ct);
        }
        return link;
    }

    /// <summary>Creates or updates the Taskel account of an imported Keycloak user. Roles and groups wait for the first sign-in.</summary>
    public async Task<KeycloakLink> ImportAsync(KeycloakUser account, CancellationToken ct = default)
    {
        var link = await LinkAsync(account, ct);
        if (link.User is { } user)
        {
            Update(user, account);
            await db.SaveChangesAsync(ct);
        }
        return link;
    }

    /// <summary>
    /// Finds the account by its Keycloak id, otherwise by its email. A local account with the same
    /// email is taken over (it keeps its tasks; its password stops working), but only on an address
    /// Keycloak verified: otherwise anyone able to set their own email in Keycloak could take over
    /// any local account. Accounts of Active Directory or of another Keycloak user are never taken.
    /// </summary>
    private async Task<KeycloakLink> LinkAsync(KeycloakUser account, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.KeycloakSubject == account.Subject, ct);
        if (account.Email is null)
            return user is null ? new KeycloakLink(null, KeycloakRefusal.NoEmail) : new KeycloakLink(user, null);

        var sameEmail = await db.Users.FirstOrDefaultAsync(u => u.Email == account.Email, ct);
        var created = false;
        if (user is null)
        {
            if (sameEmail is not null)
            {
                if (sameEmail.IsExternal) return new KeycloakLink(null, KeycloakRefusal.EmailTaken);
                if (!account.EmailVerified) return new KeycloakLink(null, KeycloakRefusal.EmailUnverified);
                user = sameEmail;
                user.PasswordHash = string.Empty;
            }
            else
            {
                user = new User { Email = account.Email };
                db.Users.Add(user);
                created = true;
            }
            user.KeycloakSubject = account.Subject;
        }
        else if (sameEmail is null && account.EmailVerified)
        {
            // The address changed in Keycloak and is free here: follow it.
            user.Email = account.Email;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with another sign-in or registration for the same address.
            db.ChangeTracker.Clear();
            return new KeycloakLink(null, KeycloakRefusal.EmailTaken);
        }
        return new KeycloakLink(user, null, created);
    }

    /// <summary>Keycloak decides the name, and the department when it has one.</summary>
    private static void Update(User user, KeycloakUser account)
    {
        if (account.DisplayName is not null) user.DisplayName = account.DisplayName;
        if (account.Department is not null) user.Department = account.Department;
    }
}
