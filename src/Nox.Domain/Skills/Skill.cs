using System.Text.Json.Nodes;
using Orleans;

namespace Nox.Domain.Skills;

[GenerateSerializer]
public class Skill
{
    [Id(0)]  public Guid Id { get; init; } = Guid.NewGuid();
    [Id(1)]  public required string Slug { get; set; }
    [Id(2)]  public required string Name { get; set; }
    [Id(3)]  public string? Description { get; set; }
    [Id(4)]  public SkillType Type { get; set; } = SkillType.SlashCommand;
    [Id(5)]  public SkillScope Scope { get; set; } = SkillScope.Global;
    [Id(6)]  public string? GroupId { get; set; }
    [Id(7)]  public Guid? OwnerAgentId { get; set; }
    [Id(8)]  public JsonObject Definition { get; set; } = new();
    [Id(9)]  public bool IsMandatory { get; set; } = false;
    [Id(10)] public SkillStatus Status { get; set; } = SkillStatus.Active;
    [Id(11)] public string? ApprovedBy { get; set; }
    [Id(12)] public int Version { get; set; } = 1;
    [Id(13)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [Id(14)] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
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
