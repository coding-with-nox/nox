using System.Text.Json.Nodes;

namespace Nox.Domain.Skills;

public class Skill
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public SkillType Type { get; set; } = SkillType.SlashCommand;
    public SkillScope Scope { get; set; } = SkillScope.Global;
    public string? GroupId { get; set; }
    public Guid? OwnerAgentId { get; set; }
    public JsonObject Definition { get; set; } = new();
    public bool IsMandatory { get; set; } = false;
    public SkillStatus Status { get; set; } = SkillStatus.Active;
    public string? ApprovedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SkillProposal
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public SkillType Type { get; init; } = SkillType.SlashCommand;
    public SkillScope Scope { get; init; } = SkillScope.Global;
    public string? GroupId { get; init; }
    public JsonObject Definition { get; init; } = new();
    public required string Justification { get; init; }
}

public class SlashCommand
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Guid SkillId { get; init; }
}
