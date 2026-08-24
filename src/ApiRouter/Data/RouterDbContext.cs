using ApiRouter.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Data;

/// <summary>EF Core context backing the router with a local SQLite database.</summary>
public class RouterDbContext : DbContext
{
    public RouterDbContext(DbContextOptions<RouterDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Target> Targets => Set<Target>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<DispatchStep> DispatchSteps => Set<DispatchStep>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Name).IsRequired().HasMaxLength(200);
            e.Property(u => u.ApiKeyHash).IsRequired().HasMaxLength(200);
            e.HasIndex(u => u.ApiKeyHash).IsUnique();
        });

        modelBuilder.Entity<Target>(e =>
        {
            e.Property(t => t.Key).IsRequired().HasMaxLength(200);
            e.HasIndex(t => t.Key).IsUnique();
        });

        modelBuilder.Entity<Rule>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.HasOne(r => r.User)
                .WithMany(u => u.Rules)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Dispatch>(e =>
        {
            e.HasOne(d => d.User)
                .WithMany(u => u.Dispatches)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(d => d.Steps)
                .WithOne(s => s.Dispatch!)
                .HasForeignKey(s => s.DispatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Schedule>(e =>
        {
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.HasOne(s => s.User)
                .WithMany(u => u.Schedules)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
