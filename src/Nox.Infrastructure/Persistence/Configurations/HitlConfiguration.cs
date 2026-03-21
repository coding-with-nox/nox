using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nox.Domain.Hitl;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Persistence.Configurations;

file static class HitlJsonConvert
{
    public static string SerializeObj(JsonObject v) => v.ToJsonString((System.Text.Json.JsonSerializerOptions?)null);
    public static JsonObject DeserializeObj(string v) => JsonNode.Parse(v, null, default)!.AsObject();
    public static string? SerializeObjNullable(JsonObject? v) => v == null ? null : v.ToJsonString((System.Text.Json.JsonSerializerOptions?)null);
    public static JsonObject? DeserializeObjNullable(string? v) => v == null ? null : JsonNode.Parse(v, null, default)!.AsObject();
}

public class HitlCheckpointConfiguration : IEntityTypeConfiguration<HitlCheckpoint>
{
    public void Configure(EntityTypeBuilder<HitlCheckpoint> builder)
    {
        builder.ToTable("hitl_checkpoints");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.FlowRunId).HasColumnName("flow_run_id");
        builder.Property(h => h.FlowNodeId).HasColumnName("flow_node_id");
        builder.Property(h => h.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(h => h.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(h => h.Title).HasColumnName("title");
        builder.Property(h => h.Description).HasColumnName("description");
        builder.Property(h => h.DecisionOptions).HasColumnName("decision_options").HasColumnType("text[]");
        builder.Property(h => h.Decision).HasColumnName("decision");
        builder.Property(h => h.DecisionBy).HasColumnName("decision_by");
        builder.Property(h => h.ExpiresAt).HasColumnName("expires_at");
        builder.Property(h => h.CreatedAt).HasColumnName("created_at");
        builder.Property(h => h.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(h => h.Context)
            .HasColumnName("context")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => HitlJsonConvert.SerializeObj(v),
                v => HitlJsonConvert.DeserializeObj(v)));

        builder.Property(h => h.DecisionPayload)
            .HasColumnName("decision_payload")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject?, string?>(
                v => HitlJsonConvert.SerializeObjNullable(v),
                v => HitlJsonConvert.DeserializeObjNullable(v)));

        builder.HasIndex(h => new { h.Status, h.CreatedAt }).HasDatabaseName("idx_hitl_pending")
            .HasFilter("status = 'Pending'");
        builder.HasIndex(h => h.FlowRunId).HasDatabaseName("idx_hitl_flow_run");
    }
}
