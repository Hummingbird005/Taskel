using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Taskel.Api.Directory;

namespace Taskel.Api.Tests;

/// <summary>
/// Runs only when a test Active Directory (for example a Samba AD DC) is described through
/// environment variables; skipped everywhere else, including CI.
/// <code>
/// TASKEL_TEST_AD_HOST=dc1.corp.example.com   TASKEL_TEST_AD_DOMAIN=corp.example.com
/// TASKEL_TEST_AD_BASE_DN=DC=corp,DC=example,DC=com
/// </code>
/// The directory needs these accounts: ion.popescu / Domain-Pass-1 (mail set, display name
/// "Ion Popescu", department "Vânzări", member of "Taskel Users" and "Taskel Managers"),
/// maria.ionescu / Domain-Pass-2 (mail set, department "Vânzări", in neither group),
/// fara.email / Domain-Pass-3 (no mail or department attribute), dezactivat / Domain-Pass-4
/// (disabled), and an empty or unrelated group "Taskel Admins".
/// </summary>
public sealed class DirectoryFactAttribute : FactAttribute
{
    public DirectoryFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKEL_TEST_AD_HOST")))
            Skip = "No test Active Directory configured (TASKEL_TEST_AD_HOST).";
    }
}

public class RealDirectoryTests
{
    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static readonly string Domain = Env("TASKEL_TEST_AD_DOMAIN", "corp.example.com");
    private static readonly string BaseDn = Env("TASKEL_TEST_AD_BASE_DN", "DC=corp,DC=example,DC=com");

    private static LdapWithSettings Create(Action<DirectoryOptions>? configure = null)
    {
        var options = new DirectoryOptions
        {
            Enabled = true,
            Host = Environment.GetEnvironmentVariable("TASKEL_TEST_AD_HOST"),
            Port = int.Parse(Env("TASKEL_TEST_AD_PORT", "636")),
            Domain = Domain,
            BaseDn = BaseDn
        };
        configure?.Invoke(options);
        return new LdapWithSettings(options);
    }

    [DirectoryFact]
    public async Task Correct_Password_Returns_The_Account_From_The_Directory()
    {
        var user = await Create().AuthenticateAsync("ion.popescu", "Domain-Pass-1");

        Assert.NotNull(user);
        Assert.Equal($"ion.popescu@{Domain}", user.Email);
        Assert.Equal("Ion Popescu", user.DisplayName);
        Assert.True(Guid.TryParse(user.DirectoryId, out var id) && id != Guid.Empty);
    }

    [DirectoryFact]
    public async Task All_User_Name_Forms_Reach_The_Same_Account()
    {
        var authenticator = Create();

        var plain = await authenticator.AuthenticateAsync("ion.popescu", "Domain-Pass-1");
        var withDomain = await authenticator.AuthenticateAsync(@"CORP\ion.popescu", "Domain-Pass-1");
        var upn = await authenticator.AuthenticateAsync($"Ion.Popescu@{Domain}", "Domain-Pass-1");

        Assert.NotNull(plain);
        // The group list is compared by content; the rest of the record by value.
        Assert.Equal(plain with { Groups = null }, withDomain! with { Groups = null });
        Assert.Equal(plain with { Groups = null }, upn! with { Groups = null });
        Assert.Equal(plain.Groups, withDomain.Groups);
        Assert.Equal(plain.Groups, upn.Groups);
        // Groups come from memberOf, named after their common name.
        Assert.Contains(plain.Groups!, g => g.Name == "Taskel Managers" && g.Dn.StartsWith("CN=Taskel Managers,", StringComparison.OrdinalIgnoreCase));
    }

    [DirectoryFact]
    public async Task Wrong_Password_Unknown_User_And_Empty_Password_Are_Refused()
    {
        var authenticator = Create();

        Assert.Null(await authenticator.AuthenticateAsync("ion.popescu", "Wrong-Pass-1"));
        Assert.Null(await authenticator.AuthenticateAsync("nu.exista", "Domain-Pass-1"));
        Assert.Null(await authenticator.AuthenticateAsync("ion.popescu", ""));
        Assert.Null(await authenticator.AuthenticateAsync("ion.popescu)(objectClass=*", "Domain-Pass-1"));
    }

    [DirectoryFact]
    public async Task Disabled_Account_Is_Refused()
    {
        Assert.Null(await Create().AuthenticateAsync("dezactivat", "Domain-Pass-4"));
    }

    [DirectoryFact]
    public async Task Account_Without_Mail_Falls_Back_To_Its_Logon_Address()
    {
        var user = await Create().AuthenticateAsync("fara.email", "Domain-Pass-3");

        Assert.NotNull(user);
        Assert.Equal($"fara.email@{Domain}", user.Email);
    }

    [DirectoryFact]
    public async Task Required_Group_Lets_Members_In_And_Keeps_Others_Out()
    {
        var authenticator = Create(o => o.RequiredGroupDn = $"cn=taskel users,CN=Users,{BaseDn}");

        Assert.NotNull(await authenticator.AuthenticateAsync("ion.popescu", "Domain-Pass-1"));
        Assert.Null(await authenticator.AuthenticateAsync("maria.ionescu", "Domain-Pass-2"));
        // Without the restriction the same account signs in.
        Assert.NotNull(await Create().AuthenticateAsync("maria.ionescu", "Domain-Pass-2"));
    }

    [DirectoryFact]
    public async Task Department_And_Role_Groups_Are_Read_From_The_Directory()
    {
        var authenticator = Create(o =>
        {
            o.ManagerGroupDn = $"CN=Taskel Managers,CN=Users,{BaseDn}";
            o.AdminGroupDn = $"CN=Taskel Admins,CN=Users,{BaseDn}";
        });

        var manager = await authenticator.AuthenticateAsync("ion.popescu", "Domain-Pass-1");
        var member = await authenticator.AuthenticateAsync("maria.ionescu", "Domain-Pass-2");

        Assert.Equal("Vânzări", manager!.Department);
        Assert.True(manager.IsManager);
        Assert.False(manager.IsAdmin);
        Assert.Equal("Vânzări", member!.Department);
        Assert.False(member.IsManager);

        // Without groups configured, the directory has no say about roles.
        var plain = await Create().AuthenticateAsync("ion.popescu", "Domain-Pass-1");
        Assert.Null(plain!.IsManager);
        Assert.Null(plain.IsAdmin);
        // An account without the attribute has no department.
        Assert.Null((await Create().AuthenticateAsync("fara.email", "Domain-Pass-3"))!.Department);
    }

    [DirectoryFact]
    public async Task Unencrypted_Connection_Is_Not_Used_By_Default()
    {
        // Port 389 without TLS: the default (LDAPS) must not silently fall back to it.
        var authenticator = Create(o => o.Port = 389);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => authenticator.AuthenticateAsync("ion.popescu", "Domain-Pass-1"));
    }

    [DirectoryFact]
    public async Task StartTls_Works_On_The_Plain_Port()
    {
        var authenticator = Create(o =>
        {
            o.Port = 389;
            o.Security = DirectorySecurity.StartTls;
        });

        Assert.NotNull(await authenticator.AuthenticateAsync("ion.popescu", "Domain-Pass-1"));
    }
}
