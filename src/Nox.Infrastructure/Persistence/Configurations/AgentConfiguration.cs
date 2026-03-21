using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nox.Domain.Agents;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Persistence.Configurations;

file static class AgentJsonConvert
{
    public static string SerializeBudget(TokenBudgetConfig v) => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null);
    public static TokenBudgetConfig DeserializeBudget(string v) => JsonSerializer.Deserialize<TokenBudgetConfig>(v, (JsonSerializerOptions?)null) ?? new TokenBudgetConfig();
    public static string SerializeObj(JsonObject v) => v.ToJsonString((JsonSerializerOptions?)null);
    public static JsonObject DeserializeObj(string v) => JsonNode.Parse(v, null, default)!.AsObject();
}

public class AgentTemplateConfiguration : IEntityTypeConfiguration<AgentTemplate>
{
    public void Configure(EntityTypeBuilder<AgentTemplate> builder)
    {
        builder.ToTable("agent_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Name).HasColumnName("name");
        builder.Property(t => t.Role).HasColumnName("role");
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.DefaultModel).HasColumnName("default_model").HasConversion<string>();
        builder.Property(t => t.SystemPromptTemplate).HasColumnName("system_prompt_template");
        builder.Property(t => t.DefaultMaxSubAgents).HasColumnName("default_max_sub_agents");
        builder.Property(t => t.SkillGroups).HasColumnName("skill_groups").HasColumnType("text[]");
        builder.Property(t => t.DefaultMcpServers).HasColumnName("default_mcp_servers").HasColumnType("text[]");
        builder.Property(t => t.IsGlobal).HasColumnName("is_global");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.Property(t => t.TokenBudgetConfig)
            .HasColumnName("token_budget_config")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<TokenBudgetConfig, string>(
                v => AgentJsonConvert.SerializeBudget(v),
                v => AgentJsonConvert.DeserializeBudget(v)));
    }
}

public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TemplateId).HasColumnName("template_id");
        builder.Property(a => a.FlowRunId).HasColumnName("flow_run_id");
        builder.Property(a => a.ParentAgentId).HasColumnName("parent_agent_id");
        builder.Property(a => a.Name).HasColumnName("name");
        builder.Property(a => a.Role).HasColumnName("role");
        builder.Property(a => a.Model).HasColumnName("model").HasConversion<string>();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(a => a.MaxSubAgents).HasColumnName("max_sub_agents");
        builder.Property(a => a.CurrentSubAgentCount).HasColumnName("current_sub_agent_count");
        builder.Property(a => a.TokensUsed).HasColumnName("tokens_used");
        builder.Property(a => a.McpServerBindings).HasColumnName("mcp_server_bindings").HasColumnType("text[]");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Property(a => a.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => AgentJsonConvert.SerializeObj(v),
                v => AgentJsonConvert.DeserializeObj(v)));

        builder.HasIndex(a => a.FlowRunId).HasDatabaseName("idx_agents_flow_run");
        builder.HasIndex(a => a.ParentAgentId).HasDatabaseName("idx_agents_parent");
    }
}
