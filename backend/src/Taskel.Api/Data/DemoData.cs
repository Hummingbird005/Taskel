using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Models;

namespace Taskel.Api.Data;

/// <summary>
/// Sample accounts and tasks for trying Taskel out: one account per kind of role, in two
/// departments, with tasks in every state the interface shows. The passwords are published in
/// docs/DEMO-ACCOUNTS.md, so this must never run on a server real people use: it only runs when
/// <c>Seed:DemoData</c> is true, only into a database that has no accounts yet, and never in the
/// Production environment (see <c>Program.cs</c>).
/// </summary>
public static class DemoData
{
    // These are not secrets: they are published in docs/DEMO-ACCOUNTS.md so testers can sign in.
    // Reviewed for SonarQube hotspot S2068 (hard-coded credentials): accepted, because seeding is
    // off by default, refused in Production, and only runs on an empty database.
    public const string AdminPassword = "Admin-Demo-2026";
    public const string ManagerPassword = "Manager-Demo-2026";
    public const string UserPassword = "User-Demo-2026";

    private const string Sales = "Vânzări";

    /// <summary>How a sample task differs from a plain one: days are counted from today.</summary>
    private sealed record Sample
    {
        public int? Due { get; init; }
        public int? Start { get; init; }
        public TaskPriority Priority { get; init; } = TaskPriority.Medium;
        public string[] Tags { get; init; } = [];
        public string? Notes { get; init; }
        public User? Assignee { get; init; }
        public Group? Group { get; init; }
        public int? DoneDaysAgo { get; init; }
    }

    /// <summary>An account as it is seeded. Listed here so the documentation and the tests read from one place.</summary>
    public record Account(string Email, string DisplayName, UserRole Role, string Department, string Password);

    public static readonly IReadOnlyList<Account> Accounts =
    [
        new("admin@taskel.example", "Cristina Vasile", UserRole.Admin, "IT", AdminPassword),
        new("manager.vanzari@taskel.example", "Ion Popescu", UserRole.Manager, Sales, ManagerPassword),
        new("manager.suport@taskel.example", "Radu Marin", UserRole.Manager, "Suport", ManagerPassword),
        new("maria.ionescu@taskel.example", "Maria Ionescu", UserRole.User, Sales, UserPassword),
        new("andrei.stan@taskel.example", "Andrei Stan", UserRole.User, Sales, UserPassword),
        new("elena.dumitru@taskel.example", "Elena Dumitru", UserRole.User, Sales, UserPassword),
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

        TaskItem Add(User owner, string title, Sample? sample = null)
        {
            sample ??= new Sample();
            var task = new TaskItem
            {
                UserId = owner.Id,
                Title = title,
                Description = sample.Notes,
                DueDate = sample.Due is { } due ? Day(due) : null,
                StartDate = sample.Start is { } start ? Day(start) : null,
                Priority = sample.Priority,
                Tags = [.. sample.Tags],
                AssigneeId = sample.Assignee?.Id,
                GroupId = sample.Group?.Id,
                // Spread out so lists sorted by creation keep a stable, readable order.
                CreatedAt = now.AddDays(-10).AddMinutes(tasks.Count)
            };
            if (sample.DoneDaysAgo is { } daysAgo)
            {
                task.IsCompleted = true;
                task.CompletedAt = now.AddDays(-daysAgo);
            }
            tasks.Add(task);
            return task;
        }

        // Ion (manager, Vânzări): his own work, in every state.
        Add(ion, "Trimite factura către client", new() { Due = -3, Priority = TaskPriority.High, Tags = ["contabilitate"] });
        Add(ion, "Reînnoiește contractul cu furnizorul", new() { Due = -1, Tags = ["juridic"] });
        Add(ion, "Pregătește prezentarea pentru ședința de luni", new() { Due = 0, Priority = TaskPriority.High, Tags = ["proiect-alfa", "ședințe"] });
        Add(ion, "Sună furnizorul pentru ofertă", new() { Due = 0, Tags = ["achiziții"], Notes = "Telefon: 0721 000 000" });
        Add(ion, "Revizuiește planul de vânzări", new() { Due = 1, Tags = ["proiect-alfa"] });
        Add(ion, "Planifică obiectivele pe trimestru", new() { Due = 5, Start = 3, Priority = TaskPriority.Low, Tags = ["proiect-alfa"] });
        Add(ion, "Actualizează lista de prețuri", new() { Tags = ["contabilitate"] });
        var onHold = Add(ion, "Semnează contractul de mentenanță", new() { Due = 2, Tags = ["juridic"], Notes = "Contractul a fost trimis de furnizor. De verificat clauza de penalități înainte de semnare." });
        onHold.WaitingReason = "Aștept avizul departamentului juridic pentru clauza de penalități.";
        onHold.WaitingSince = now.AddDays(-1);
        var room = Add(ion, "Rezervă sala de conferințe", new() { DoneDaysAgo = 0, Notes = "Pentru ședința de luni, 10 persoane, cu proiector." });
        room.CompletionResponse = "Rezervată sala Mare de la 10:00 la 12:00. Proiectorul e verificat.";
        Add(ion, "Trimite raportul lunar", new() { DoneDaysAgo = 1 });
        Add(ion, "Comandă consumabile", new() { DoneDaysAgo = 2 });
        Add(ion, "Întâlnire cu echipa", new() { DoneDaysAgo = 4 });

        // A task that came from an email: completing it needs a reply to the sender.
        var fromEmail = Add(ion, "Cerere ofertă servicii consultanță", new() { Priority = TaskPriority.High, Tags = ["email"], Notes = "De la: Ana Popescu <ana.popescu@client.example>\n\nBună ziua,\n\nVă rog să ne trimiteți o ofertă până vineri.\n\nMulțumesc,\nAna" });
        fromEmail.SourceEmailAddress = "ana.popescu@client.example";
        fromEmail.SourceEmailSubject = "Cerere ofertă servicii consultanță";
        fromEmail.SourceEmailMessageId = "<demo-1@client.example>";

        // Delegated by Ion: created by him, given to someone else. He still owns them.
        var report = Add(ion, "Pregătește raportul trimestrial", new() { Due = 2, Priority = TaskPriority.High, Tags = ["raportare"], Assignee = maria });
        Add(ion, "Actualizează prezentarea de produs", new() { Due = 5, Assignee = andrei });
        Add(ion, "Sună clientul Alfa pentru feedback", new() { Due = -1, Assignee = elena });
        // Given to a group: every member sees it until one of them takes it.
        Add(ion, "Ofertă pentru licitația Primăriei", new() { Due = 3, Priority = TaskPriority.High, Group = offers });

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
        Add(maria, "Ofertă pentru clientul Beta", new() { Due = -4, Start = -6, Priority = TaskPriority.High, Notes = "Clientul vrea discount de volum.\nDe verificat cu Ion marja minimă." });
        var complaint = Add(maria, "Răspunde la reclamația #482", new() { Due = -1, Notes = "Clientul spune că livrarea a sosit incompletă: lipsesc două cutii din comanda 7731." });
        complaint.WaitingReason = "Aștept confirmarea depozitului despre ce s-a încărcat în camion.";
        complaint.WaitingSince = now.AddDays(-2);
        Add(maria, "Actualizează CRM-ul după târg", new() { Due = 0 });
        Add(maria, "Demo pentru clientul Gamma", new() { Due = 4, Start = 2, Priority = TaskPriority.High, Tags = ["demo"] });
        Add(maria, "Pregătește standul pentru târg", new() { Due = 4, Start = 1 });
        Add(maria, "Contract cadru cu Epsilon", new() { Due = 8, Start = 3, Priority = TaskPriority.High });
        Add(maria, "Contract semnat cu Delta", new() { DoneDaysAgo = 0 });
        Add(maria, "Ofertă trimisă către Omega", new() { DoneDaysAgo = 2 });
        Add(andrei, "Prospectare zona de vest", new() { Due = 2, Start = 0 });
        Add(andrei, "Pregătește mostrele pentru expoziție", new() { Due = 9, Start = 7 });
        Add(andrei, "Vizită la clientul Sigma", new() { DoneDaysAgo = 0 });
        Add(andrei, "Ofertă Kappa", new() { DoneDaysAgo = 1 });
        Add(andrei, "Negociere Tau", new() { DoneDaysAgo = 3 });
        Add(elena, "Studiu de piață concurență", new() { Due = 14, Start = 8, Priority = TaskPriority.Low });
        Add(elena, "Newsletter noiembrie", new() { DoneDaysAgo = 3 });

        // The other department, which Ion cannot see and Radu can.
        Add(radu, "Planifică turele de weekend", new() { Due = 1 });
        Add(radu, "Tichet #1021: imprimanta nu merge", new() { Due = 0, Assignee = dan });
        Add(dan, "Migrare cutii poștale", new() { Due = -2, Priority = TaskPriority.High });
        Add(dan, "Actualizează inventarul de laptopuri", new() { Due = 6, Start = 4 });

        Add(admin, "Verifică backup-ul săptămânal", new() { Due = 2, Tags = ["mentenanță"] });

        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
