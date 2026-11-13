using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Models;

namespace Taskel.Api.Data;

/// <summary>
/// Sample accounts and tasks for trying Taskel out: one account per kind of role, in two
/// departments, with tasks in every state the interface shows. The passwords are published in
/// docs/DEMO-ACCOUNTS.md, so this must never run on a server real people use: it only runs when
/// <c>Seed:DemoData</c> is true, and only into a database that has no accounts yet.
/// </summary>
public static class DemoData
{
    public const string AdminPassword = "Admin-Demo-2026";
    public const string ManagerPassword = "Manager-Demo-2026";
    public const string UserPassword = "User-Demo-2026";

    /// <summary>An account as it is seeded. Listed here so the documentation and the tests read from one place.</summary>
    public record Account(string Email, string DisplayName, UserRole Role, string Department, string Password);

    public static readonly IReadOnlyList<Account> Accounts =
    [
        new("admin@taskel.example", "Cristina Vasile", UserRole.Admin, "IT", AdminPassword),
        new("manager.vanzari@taskel.example", "Ion Popescu", UserRole.Manager, "Vânzări", ManagerPassword),
        new("manager.suport@taskel.example", "Radu Marin", UserRole.Manager, "Suport", ManagerPassword),
        new("maria.ionescu@taskel.example", "Maria Ionescu", UserRole.User, "Vânzări", UserPassword),
        new("andrei.stan@taskel.example", "Andrei Stan", UserRole.User, "Vânzări", UserPassword),
        new("elena.dumitru@taskel.example", "Elena Dumitru", UserRole.User, "Vânzări", UserPassword),
        new("dan.pavel@taskel.example", "Dan Pavel", UserRole.User, "Suport", UserPassword),
    ];

    /// <summary>
    /// Adds the sample data when the database has no accounts. Returns false, changing nothing,
    /// when there is already at least one.
    /// </summary>
    public static async Task<bool> SeedAsync(TaskelDbContext db, IPasswordHasher<User> hasher, DateOnly today, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return false;

        var users = Accounts.ToDictionary(a => a.Email, a =>
        {
            var user = new User { Email = a.Email, DisplayName = a.DisplayName, Role = a.Role, Department = a.Department };
            user.PasswordHash = hasher.HashPassword(user, a.Password);
            return user;
        });
        db.Users.AddRange(users.Values);

        var admin = users["admin@taskel.example"];
        var ion = users["manager.vanzari@taskel.example"];
        var radu = users["manager.suport@taskel.example"];
        var maria = users["maria.ionescu@taskel.example"];
        var andrei = users["andrei.stan@taskel.example"];
        var elena = users["elena.dumitru@taskel.example"];
        var dan = users["dan.pavel@taskel.example"];

        var offers = new Group
        {
            Name = "Ofertare",
            Kind = GroupKind.App,
            CreatedById = ion.Id,
            Members = [new GroupMember { UserId = maria.Id }, new GroupMember { UserId = andrei.Id }, new GroupMember { UserId = elena.Id }]
        };
        db.Groups.Add(offers);

        DateOnly Day(int offset) => today.AddDays(offset);
        var now = DateTime.UtcNow;
        var tasks = new List<TaskItem>();

        TaskItem Add(User owner, string title, int? due = null, int? start = null, TaskPriority priority = TaskPriority.Medium,
            string[]? tags = null, string? notes = null, User? assignee = null, Group? group = null, int? doneDaysAgo = null)
        {
            var task = new TaskItem
            {
                UserId = owner.Id,
                Title = title,
                Description = notes,
                DueDate = due is null ? null : Day(due.Value),
                StartDate = start is null ? null : Day(start.Value),
                Priority = priority,
                Tags = [.. tags ?? []],
                AssigneeId = assignee?.Id,
                GroupId = group?.Id,
                // Spread out so lists sorted by creation keep a stable, readable order.
                CreatedAt = now.AddDays(-10).AddMinutes(tasks.Count)
            };
            if (doneDaysAgo is { } daysAgo)
            {
                task.IsCompleted = true;
                task.CompletedAt = now.AddDays(-daysAgo);
            }
            tasks.Add(task);
            return task;
        }

        // Ion (manager, Vânzări): his own work, in every state.
        Add(ion, "Trimite factura către client", due: -3, priority: TaskPriority.High, tags: ["contabilitate"]);
        Add(ion, "Reînnoiește contractul cu furnizorul", due: -1, tags: ["juridic"]);
        Add(ion, "Pregătește prezentarea pentru ședința de luni", due: 0, priority: TaskPriority.High, tags: ["proiect-alfa", "ședințe"]);
        Add(ion, "Sună furnizorul pentru ofertă", due: 0, tags: ["achiziții"], notes: "Telefon: 0721 000 000");
        Add(ion, "Revizuiește planul de vânzări", due: 1, tags: ["proiect-alfa"]);
        Add(ion, "Planifică obiectivele pe trimestru", due: 5, start: 3, priority: TaskPriority.Low, tags: ["proiect-alfa"]);
        Add(ion, "Actualizează lista de prețuri", tags: ["contabilitate"]);
        Add(ion, "Rezervă sala de conferințe", doneDaysAgo: 0);
        Add(ion, "Trimite raportul lunar", doneDaysAgo: 1);
        Add(ion, "Comandă consumabile", doneDaysAgo: 2);
        Add(ion, "Întâlnire cu echipa", doneDaysAgo: 4);

        // A task that came from an email: completing it needs a reply to the sender.
        var fromEmail = Add(ion, "Cerere ofertă servicii consultanță", priority: TaskPriority.High, tags: ["email"],
            notes: "De la: Ana Popescu <ana.popescu@client.example>\n\nBună ziua,\n\nVă rog să ne trimiteți o ofertă până vineri.\n\nMulțumesc,\nAna");
        fromEmail.SourceEmailAddress = "ana.popescu@client.example";
        fromEmail.SourceEmailSubject = "Cerere ofertă servicii consultanță";
        fromEmail.SourceEmailMessageId = "<demo-1@client.example>";

        // Delegated by Ion: created by him, given to someone else. He still owns them.
        var report = Add(ion, "Pregătește raportul trimestrial", due: 2, priority: TaskPriority.High, tags: ["raportare"], assignee: maria);
        Add(ion, "Actualizează prezentarea de produs", due: 5, assignee: andrei);
        Add(ion, "Sună clientul Alfa pentru feedback", due: -1, assignee: elena);
        // Given to a group: every member sees it until one of them takes it.
        Add(ion, "Ofertă pentru licitația Primăriei", due: 3, priority: TaskPriority.High, group: offers);

        // Forwarded: Ion gave it to Maria, and Maria passed it on to Andrei. She can still read it.
        report.AssigneeId = andrei.Id;
        report.Forwards.Add(new TaskForward
        {
            TaskId = report.Id,
            FromUserId = maria.Id,
            ToUserId = andrei.Id,
            Note = "Sunt la târg toată săptămâna. Datele sunt în CRM, la rapoarte.",
            At = now.AddDays(-1)
        });

        // The team's own work, so the manager's workload and calendar screens have something to show.
        Add(maria, "Ofertă pentru clientul Beta", due: -4, start: -6, priority: TaskPriority.High,
            notes: "Clientul vrea discount de volum.\nDe verificat cu Ion marja minimă.");
        Add(maria, "Răspunde la reclamația #482", due: -1);
        Add(maria, "Actualizează CRM-ul după târg", due: 0);
        Add(maria, "Demo pentru clientul Gamma", due: 4, start: 2, priority: TaskPriority.High, tags: ["demo"]);
        Add(maria, "Pregătește standul pentru târg", due: 4, start: 1);
        Add(maria, "Contract cadru cu Epsilon", due: 8, start: 3, priority: TaskPriority.High);
        Add(maria, "Contract semnat cu Delta", doneDaysAgo: 0);
        Add(maria, "Ofertă trimisă către Omega", doneDaysAgo: 2);
        Add(andrei, "Prospectare zona de vest", due: 2, start: 0);
        Add(andrei, "Pregătește mostrele pentru expoziție", due: 9, start: 7);
        Add(andrei, "Vizită la clientul Sigma", doneDaysAgo: 0);
        Add(andrei, "Ofertă Kappa", doneDaysAgo: 1);
        Add(andrei, "Negociere Tau", doneDaysAgo: 3);
        Add(elena, "Studiu de piață concurență", due: 14, start: 8, priority: TaskPriority.Low);
        Add(elena, "Newsletter noiembrie", doneDaysAgo: 3);

        // The other department, which Ion cannot see and Radu can.
        Add(radu, "Planifică turele de weekend", due: 1);
        Add(radu, "Tichet #1021: imprimanta nu merge", due: 0, assignee: dan);
        Add(dan, "Migrare cutii poștale", due: -2, priority: TaskPriority.High);
        Add(dan, "Actualizează inventarul de laptopuri", due: 6, start: 4);

        Add(admin, "Verifică backup-ul săptămânal", due: 2, tags: ["mentenanță"]);

        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
