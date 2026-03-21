using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nox.Domain.Skills;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Persistence.Configurations;

file static class SkillJsonConvert
{
    public static string SerializeObj(JsonObject v) => v.ToJsonString((System.Text.Json.JsonSerializerOptions?)null);
    public static JsonObject DeserializeObj(string v) => JsonNode.Parse(v, null, default)!.AsObject();
}

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Slug).HasColumnName("slug");
        builder.Property(s => s.Name).HasColumnName("name");
        builder.Property(s => s.Description).HasColumnName("description");
        builder.Property(s => s.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(s => s.Scope).HasColumnName("scope").HasConversion<string>();
        builder.Property(s => s.GroupId).HasColumnName("group_id");
        builder.Property(s => s.OwnerAgentId).HasColumnName("owner_agent_id");
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(s => s.ApprovedBy).HasColumnName("approved_by");
        builder.Property(s => s.Version).HasColumnName("version");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.Property(s => s.Definition)
            .HasColumnName("definition")
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<JsonObject, string>(
                v => SkillJsonConvert.SerializeObj(v),
                v => SkillJsonConvert.DeserializeObj(v)));

        builder.HasIndex(s => new { s.Scope, s.Status }).HasDatabaseName("idx_skills_scope");
    }
}
