using Microsoft.EntityFrameworkCore;
using Nox.Domain.Agents;
using Nox.Domain.Audit;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Domain.Memory;
using Nox.Domain.Messages;
using Nox.Domain.Projects;
using Nox.Domain.Skills;
using Nox.Domain.Mcp;

namespace Nox.Infrastructure.Persistence;

public class NoxDbContext(DbContextOptions<NoxDbContext> options) : DbContext(options)
{
    public DbSet<Project>         Projects       => Set<Project>();
    public DbSet<AgentTemplate>   AgentTemplates => Set<AgentTemplate>();
    public DbSet<Agent>           Agents         => Set<Agent>();
    public DbSet<Flow>            Flows          => Set<Flow>();
    public DbSet<FlowRun>         FlowRuns       => Set<FlowRun>();
    public DbSet<FlowRunConfig>   FlowRunConfigs => Set<FlowRunConfig>();
    public DbSet<AgentTask>       AgentTasks     => Set<AgentTask>();
    public DbSet<HitlCheckpoint>  HitlCheckpoints => Set<HitlCheckpoint>();
    public DbSet<Skill>           Skills         => Set<Skill>();
    public DbSet<McpServerInfo>   McpServers     => Set<McpServerInfo>();
    public DbSet<MemoryEntry>     ProjectMemory  => Set<MemoryEntry>();
    public DbSet<AcpMessage>      AcpMessages    => Set<AcpMessage>();

    // GDPR & AI Act compliance
    public DbSet<AiAuditEntry>    AiAuditLog     => Set<AiAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoxDbContext).Assembly);

        // AiAuditEntry — immutable, no FK constraints, retention-based
        modelBuilder.Entity<AiAuditEntry>(e =>
        {
            e.ToTable("ai_audit_log");
            e.HasKey(a => a.Id);
            e.Property(a => a.EventType).HasMaxLength(100);
            e.Property(a => a.ModelUsed).HasMaxLength(100);
            e.Property(a => a.DecidedBy).HasMaxLength(200);
            e.Property(a => a.Decision).HasMaxLength(200);
            e.Property(a => a.InputHash).HasMaxLength(64);
            e.Property(a => a.OutputHash).HasMaxLength(64);
            e.Property(a => a.Summary).HasMaxLength(500);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.AgentId);
            e.HasIndex(a => a.RetainUntil);
        });
    }
}

// AgentTask entity (in persistence layer since Domain uses TaskStatus which conflicts with System.Threading.Tasks)
public class AgentTask
{
    public Guid   Id               { get; init; } = Guid.NewGuid();
    public required Guid   FlowRunId      { get; init; }
    public required string FlowNodeId     { get; init; }
    public required Guid   AssignedAgentId { get; init; }
    public Guid?  ParentTaskId    { get; init; }
    public Domain.TaskStatus Status { get; set; } = Domain.TaskStatus.Pending;
    public string Input            { get; set; } = "{}";
    public string? Output          { get; set; }
    public string ToolCalls        { get; set; } = "[]";
    public int    TokensUsed       { get; set; }
    public DateTimeOffset? StartedAt   { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error           { get; set; }
}
