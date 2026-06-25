using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Persistence;

/// <summary>EF Core context backing WheelHouse on local SQLite.</summary>
public class WheelHouseDbContext : DbContext
{
    public WheelHouseDbContext(DbContextOptions<WheelHouseDbContext> options) : base(options) { }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<AgentSession> Sessions => Set<AgentSession>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<CommandRule> CommandRules => Set<CommandRule>();
    public DbSet<CodeIndexEntry> CodeIndex => Set<CodeIndexEntry>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();
    public DbSet<AppConfiguration> AppConfig => Set<AppConfiguration>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Workspace>(e =>
        {
            e.HasIndex(w => w.AbsolutePath).IsUnique();
            e.Property(w => w.Name).IsRequired();
        });

        b.Entity<AgentSession>(e =>
        {
            e.HasIndex(s => s.SessionId);
            e.HasOne(s => s.Workspace)
                .WithMany(w => w.Sessions)
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskItem>(e =>
        {
            e.HasOne(t => t.AgentSession)
                .WithMany(s => s.Tasks)
                .HasForeignKey(t => t.AgentSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CommandRule>(e =>
        {
            e.HasOne(r => r.Workspace)
                .WithMany(w => w.CommandRules)
                .HasForeignKey(r => r.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CodeIndexEntry>(e =>
        {
            e.HasIndex(c => new { c.RepositoryPath, c.FilePath });
        });

        b.Entity<PromptTemplate>(e =>
        {
            e.HasIndex(t => t.Name);
            e.Property(t => t.Name).IsRequired();
        });

        b.Entity<SessionEvent>(e =>
        {
            e.HasIndex(s => new { s.AgentSessionId, s.Id });
            e.HasOne(s => s.AgentSession)
                .WithMany(a => a.Events)
                .HasForeignKey(s => s.AgentSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(b);
    }
}
