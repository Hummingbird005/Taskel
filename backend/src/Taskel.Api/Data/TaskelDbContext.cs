using Microsoft.EntityFrameworkCore;
using Taskel.Api.Models;

namespace Taskel.Api.Data;

public class TaskelDbContext(DbContextOptions<TaskelDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>(task =>
        {
            task.ToTable("tasks");
            task.HasKey(t => t.Id);
            task.Property(t => t.Title).HasMaxLength(200).IsRequired();
            task.Property(t => t.Description).HasMaxLength(2000);
            task.Property(t => t.CreatedAt).IsRequired();
            task.HasIndex(t => t.CreatedAt);
        });
    }
}
