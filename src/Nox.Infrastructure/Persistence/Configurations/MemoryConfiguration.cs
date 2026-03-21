using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nox.Domain.Memory;
using Nox.Domain.Messages;
using Nox.Domain.Projects;
using Nox.Domain.Mcp;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Persistence.Configurations;

file static class MemJsonConvert
{
    public static string SerializeObj(JsonObject v) => v.ToJsonString((System.Text.Json.JsonSerializerOptions?)null);
    public static JsonObject DeserializeObj(string v) => JsonNode.Parse(v, null, default)!.AsObject();
}

public class MemoryEntryConfiguration : IEntityTypeConfiguration<MemoryEntry>
{
    public void Configure(EntityTypeBuilder<MemoryEntry> builder)
    {
        builder.ToTable("project_memory");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ProjectId).HasColumnName("project_id");
        builder.Property(m => m.AgentId).HasColumnName("agent_id");
        builder.Property(m => m.Content).HasColumnName("content");
        builder.Property(m => m.ContentType).HasColumnName("content_type").HasConversion<string>();
        builder.Property(m => m.QdrantPointId).HasColumnName("qdrant_point_id");
        builder.Property(m => m.TokenCount).HasColumnName("token_count");
        builder.Property(m => m.Tags).HasColumnName("tags").HasColumnType("text[]");
        builder.Property(m => m.Importance).HasColumnName("importance");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(m => new { m.ProjectId, m.Importance }).HasDatabaseName("idx_memory_project");
        builder.HasIndex(m => m.AgentId).HasDatabaseName("idx_memory_agent");
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Name).HasColumnName("name");
        builder.Property(p => p.Description).HasColumnName("description");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
    }
}

public class AcpMessageConfiguration : IEntityTypeConfiguration<AcpMessage>
{
    public void Configure(EntityTypeBuilder<AcpMessage> builder)
    {
        builder.ToTable("acp_messages");
        builder.HasKey(m => new { m.Id, m.Timestamp });
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CorrelationId).HasColumnName("correlation_id");
        builder.Property(m => m.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(m => m.Topic).HasColumnName("topic");
        builder.Property(m => m.Timestamp).HasColumnName("timestamp");

        builder.Property(m => m.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => MemJsonConvert.SerializeObj(v),
                v => MemJsonConvert.DeserializeObj(v)));

        // From/To stored as separate columns via owned types
        builder.Ignore(m => m.From);
        builder.Ignore(m => m.To);
        builder.Ignore(m => m.Ttl);

        // Add flattened columns for From/To
        builder.Property<Guid?>("FromAgentId").HasColumnName("from_agent_id");
        builder.Property<Guid?>("FromFlowRunId").HasColumnName("from_flow_run_id");
        builder.Property<Guid?>("ToAgentId").HasColumnName("to_agent_id");
        builder.Property<Guid?>("ToFlowRunId").HasColumnName("to_flow_run_id");
    }
}

public class McpServerInfoConfiguration : IEntityTypeConfiguration<McpServerInfo>
{
    public void Configure(EntityTypeBuilder<McpServerInfo> builder)
    {
        builder.ToTable("mcp_servers");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.Name).HasColumnName("name");
        builder.Property(m => m.Description).HasColumnName("description");
        builder.Property(m => m.Transport).HasColumnName("transport").HasConversion<string>();
        builder.Property(m => m.EndpointUrl).HasColumnName("endpoint_url");
        builder.Property(m => m.DockerImage).HasColumnName("docker_image");
        builder.Property(m => m.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(m => m.ProposedByAgentId).HasColumnName("proposed_by_agent_id");
        builder.Property(m => m.ApprovedBy).HasColumnName("approved_by");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.Property(m => m.EnvironmentVars)
            .HasColumnName("environment_vars")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => MemJsonConvert.SerializeObj(v),
                v => MemJsonConvert.DeserializeObj(v)));
    }
}
