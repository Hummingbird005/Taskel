using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Taskel.Api.Integrations;
using Taskel.Api.Models;

namespace Taskel.Api.Data;

public class TaskelDbContext(DbContextOptions<TaskelDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<TaskForward> TaskForwards => Set<TaskForward>();
    public DbSet<TaskMessage> TaskMessages => Set<TaskMessage>();
    public DbSet<TaskAttachment> Attachments => Set<TaskAttachment>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<IntegrationSetting> IntegrationSettings => Set<IntegrationSetting>();
    /// <summary>The keys that protect sign-in cookies and the stored integration secrets (ASP.NET Core Data Protection).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(254).IsRequired();
            user.Property(u => u.PasswordHash).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(256);
            user.Property(u => u.DirectoryId).HasMaxLength(64);
            user.HasIndex(u => u.DirectoryId).IsUnique();
            user.Property(u => u.KeycloakSubject).HasMaxLength(255);
            user.HasIndex(u => u.KeycloakSubject).IsUnique();
            user.Ignore(u => u.IsExternal);
            user.Property(u => u.Department).HasMaxLength(User.MaxDepartmentLength);
            user.HasIndex(u => u.Department);
            user.Property(u => u.DashboardWidgets).HasColumnType("text[]");
            user.Property(u => u.ShowDueAlert).HasDefaultValue(true);
            user.Property(u => u.EmailNotifications).HasDefaultValue(true);
        });

        modelBuilder.Entity<Group>(group =>
        {
            group.ToTable("groups");
            group.HasKey(g => g.Id);
            group.Property(g => g.Name).HasMaxLength(Group.MaxNameLength).IsRequired();
            group.Property(g => g.DirectoryDn).HasMaxLength(1024);
            group.HasIndex(g => g.DirectoryDn).IsUnique();
            group.HasOne<User>().WithMany().HasForeignKey(g => g.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GroupMember>(member =>
        {
            member.ToTable("group_members");
            member.HasKey(m => new { m.GroupId, m.UserId });
            member.HasOne<Group>().WithMany(g => g.Members).HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            member.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            member.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<TaskForward>(forward =>
        {
            forward.ToTable("task_forwards");
            forward.HasKey(f => f.Id);
            // The id is set in code; a forward added to a loaded task must be seen as new.
            forward.Property(f => f.Id).ValueGeneratedNever();
            // A return carries the resolution, which can be as long as a completion response.
            forward.Property(f => f.Note).HasMaxLength(4000);
            forward.HasOne<TaskItem>().WithMany(t => t.Forwards).HasForeignKey(f => f.TaskId).OnDelete(DeleteBehavior.Cascade);
            forward.HasOne(f => f.From).WithMany().HasForeignKey(f => f.FromUserId).OnDelete(DeleteBehavior.Cascade);
            forward.HasOne(f => f.ToUser).WithMany().HasForeignKey(f => f.ToUserId).OnDelete(DeleteBehavior.SetNull);
            forward.HasOne(f => f.ToGroup).WithMany().HasForeignKey(f => f.ToGroupId).OnDelete(DeleteBehavior.SetNull);
            forward.HasIndex(f => f.FromUserId);
        });

        modelBuilder.Entity<ApiToken>(token =>
        {
            token.ToTable("api_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.Name).HasMaxLength(50).IsRequired();
            token.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskAttachment>(attachment =>
        {
            attachment.ToTable("task_attachments");
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.FileName).HasMaxLength(255).IsRequired();
            attachment.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
            attachment.Property(a => a.StoredName).HasMaxLength(32).IsRequired();
            attachment.HasOne<TaskItem>().WithMany(t => t.Attachments).HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
            attachment.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.SetNull);
            attachment.HasOne<TaskMessage>().WithMany().HasForeignKey(a => a.AnswerMessageId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IntegrationSetting>(setting =>
        {
            setting.ToTable("integration_settings");
            setting.HasKey(s => s.Kind);
            setting.Property(s => s.Kind).HasMaxLength(32);
            setting.Property(s => s.Settings).IsRequired();
            setting.HasOne<User>().WithMany().HasForeignKey(s => s.UpdatedById).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DataProtectionKey>(key => key.ToTable("data_protection_keys"));

        modelBuilder.Entity<AppSetting>(setting =>
        {
            setting.ToTable("app_settings");
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(100);
            setting.Property(s => s.Value).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<TaskItem>(task =>
        {
            task.Property(t => t.WaitingReason).HasMaxLength(1000);
            task.Ignore(t => t.State);
            task.ToTable("tasks");
            task.HasKey(t => t.Id);
            task.Property(t => t.Title).HasMaxLength(200).IsRequired();
            task.Property(t => t.Description).HasMaxLength(2000);
            task.Property(t => t.Priority).HasDefaultValue(TaskPriority.Medium).HasSentinel((TaskPriority)(-1));
            task.Property(t => t.Tags).HasColumnType("text[]").HasDefaultValueSql("'{}'");
            task.Property(t => t.CreatedAt).IsRequired();
            task.Property(t => t.SourceEmailAddress).HasMaxLength(320);
            task.Property(t => t.SourceEmailSubject).HasMaxLength(500);
            task.Property(t => t.SourceEmailMessageId).HasMaxLength(998);
            task.Property(t => t.CompletionResponse).HasMaxLength(4000);
            task.Property(t => t.Instructions).HasMaxLength(2000);
            task.Ignore(t => t.RequiresCompletionReply);
            task.HasOne(t => t.Owner).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            task.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            task.HasIndex(t => t.AssigneeId);
            task.HasOne(t => t.Group).WithMany().HasForeignKey(t => t.GroupId).OnDelete(DeleteBehavior.SetNull);
            task.HasIndex(t => t.GroupId);
            task.HasIndex(t => new { t.UserId, t.CreatedAt });
            // A distributed copy outlives the task it came from; it just stops being linked to it.
            task.HasOne(t => t.ParentTask).WithMany(t => t.Distributions).HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.SetNull);
            task.HasIndex(t => t.ParentTaskId);
        });

        modelBuilder.Entity<TaskMessage>(message =>
        {
            message.ToTable("task_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Text).HasMaxLength(4000);
            message.HasOne<TaskItem>().WithMany().HasForeignKey(m => m.TaskId).OnDelete(DeleteBehavior.Cascade);
            message.HasOne(m => m.RelatedTask).WithMany().HasForeignKey(m => m.RelatedTaskId).OnDelete(DeleteBehavior.SetNull);
            message.HasOne(m => m.Author).WithMany().HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.SetNull);
            message.HasIndex(m => new { m.TaskId, m.At });
            message.HasIndex(m => m.RelatedTaskId);
        });
    }
}
