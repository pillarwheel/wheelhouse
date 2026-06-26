using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Persistence;

/// <summary>EF Core context backing WheelHouse on local SQLite.</summary>
public class WheelHouseDbContext : DbContext
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public WheelHouseDbContext(DbContextOptions<WheelHouseDbContext> options) : base(options) { }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<AgentSession> Sessions => Set<AgentSession>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<CommandRule> CommandRules => Set<CommandRule>();
    public DbSet<CodeIndexEntry> CodeIndex => Set<CodeIndexEntry>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();
    public DbSet<AppConfiguration> AppConfig => Set<AppConfiguration>();
    public DbSet<SessionTemplate> SessionTemplates => Set<SessionTemplate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Workspace>(e =>
        {
            e.HasIndex(w => w.AbsolutePath).IsUnique();
            e.Property(w => w.Name).IsRequired();
        });

        b.Entity<SessionTemplate>(e =>
        {
            e.Property(t => t.Name).IsRequired();

            var stepsConverter = new ValueConverter<List<FlowStepConfiguration>, string>(
                v => JsonSerializer.Serialize(v, _jsonOpts),
                v => JsonSerializer.Deserialize<List<FlowStepConfiguration>>(v, _jsonOpts) ?? new());

            // JSON-based comparer so EF change-tracks edits to the Steps collection.
            var stepsComparer = new ValueComparer<List<FlowStepConfiguration>>(
                (a, b) => JsonSerializer.Serialize(a, _jsonOpts) == JsonSerializer.Serialize(b, _jsonOpts),
                v => v == null ? 0 : JsonSerializer.Serialize(v, _jsonOpts).GetHashCode(),
                v => JsonSerializer.Deserialize<List<FlowStepConfiguration>>(
                        JsonSerializer.Serialize(v, _jsonOpts), _jsonOpts) ?? new());

            e.Property(t => t.Steps)
                .HasColumnName("StepsJson")
                .HasConversion(stepsConverter, stepsComparer);
        });

        b.Entity<AgentSession>(e =>
        {
            e.HasIndex(s => s.SessionId);
            e.HasOne(s => s.Workspace)
                .WithMany(w => w.Sessions)
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Template)
                .WithMany()
                .HasForeignKey(s => s.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
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
