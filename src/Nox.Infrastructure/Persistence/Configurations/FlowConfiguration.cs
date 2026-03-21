using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nox.Domain.Flows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Persistence.Configurations;

file static class JsonConvert
{
    public static string Serialize(FlowGraph v) => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null);
    public static FlowGraph DeserializeFlowGraph(string v) => JsonSerializer.Deserialize<FlowGraph>(v, (JsonSerializerOptions?)null) ?? new FlowGraph();
    public static string SerializeObj(JsonObject v) => v.ToJsonString((JsonSerializerOptions?)null);
    public static JsonObject DeserializeObj(string v) => JsonNode.Parse(v, null, default)!.AsObject();
}

public class FlowConfiguration : IEntityTypeConfiguration<Flow>
{
    public void Configure(EntityTypeBuilder<Flow> builder)
    {
        builder.ToTable("flows");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.Name).HasColumnName("name").IsRequired();
        builder.Property(f => f.Description).HasColumnName("description");
        builder.Property(f => f.ProjectId).HasColumnName("project_id");
        builder.Property(f => f.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(f => f.Version).HasColumnName("version");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.CreatedBy).HasColumnName("created_by");

        builder.Property(f => f.Graph)
            .HasColumnName("graph")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<FlowGraph, string>(
                v => JsonConvert.Serialize(v),
                v => JsonConvert.DeserializeFlowGraph(v)));

        builder.Property(f => f.Variables)
            .HasColumnName("variables")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => JsonConvert.SerializeObj(v),
                v => JsonConvert.DeserializeObj(v)));

        builder.HasIndex(f => f.ProjectId).HasDatabaseName("idx_flows_project");
        builder.HasIndex(f => f.Status).HasDatabaseName("idx_flows_status");
    }
}

public class FlowRunConfiguration : IEntityTypeConfiguration<FlowRun>
{
    public void Configure(EntityTypeBuilder<FlowRun> builder)
    {
        builder.ToTable("flow_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.FlowId).HasColumnName("flow_id");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(r => r.CurrentNodeIds).HasColumnName("current_node_ids").HasColumnType("text[]");
        builder.Property(r => r.StartedAt).HasColumnName("started_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.Error).HasColumnName("error");

        builder.Property(r => r.Variables)
            .HasColumnName("variables")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => JsonConvert.SerializeObj(v),
                v => JsonConvert.DeserializeObj(v)));

        builder.HasIndex(r => r.FlowId).HasDatabaseName("idx_flow_runs_flow");
        builder.HasIndex(r => r.Status).HasDatabaseName("idx_flow_runs_status");
    }
}
