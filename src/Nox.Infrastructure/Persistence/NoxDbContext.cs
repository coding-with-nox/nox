using Microsoft.EntityFrameworkCore;
using Nox.Domain.Agents;
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
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowRun> FlowRuns => Set<FlowRun>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<HitlCheckpoint> HitlCheckpoints => Set<HitlCheckpoint>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<McpServerInfo> McpServers => Set<McpServerInfo>();
    public DbSet<MemoryEntry> ProjectMemory => Set<MemoryEntry>();
    public DbSet<AcpMessage> AcpMessages => Set<AcpMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoxDbContext).Assembly);
    }
}

// AgentTask entity (in persistence layer since Domain uses TaskStatus which conflicts with System.Threading.Tasks.Task)
public class AgentTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FlowRunId { get; init; }
    public required string FlowNodeId { get; init; }
    public required Guid AssignedAgentId { get; init; }
    public Guid? ParentTaskId { get; init; }
    public Domain.TaskStatus Status { get; set; } = Domain.TaskStatus.Pending;
    public string Input { get; set; } = "{}";
    public string? Output { get; set; }
    public string ToolCalls { get; set; } = "[]";
    public int TokensUsed { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
