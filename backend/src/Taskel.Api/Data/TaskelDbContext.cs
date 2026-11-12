using Microsoft.EntityFrameworkCore;
using Taskel.Api.Models;

namespace Taskel.Api.Data;

public class TaskelDbContext(DbContextOptions<TaskelDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

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
            user.Property(u => u.DashboardWidgets).HasColumnType("text[]");
            user.Property(u => u.ShowDueAlert).HasDefaultValue(true);
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

        modelBuilder.Entity<TaskItem>(task =>
        {
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
            task.Ignore(t => t.RequiresCompletionReply);
            task.HasOne(t => t.Owner).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            task.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            task.HasIndex(t => t.AssigneeId);
            task.HasIndex(t => new { t.UserId, t.CreatedAt });
        });
    }
}
