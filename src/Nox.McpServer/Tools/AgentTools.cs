using ModelContextProtocol.Server;
using Nox.Domain.Agents;
using Nox.Domain.Skills;
using System.ComponentModel;
using System.Text.Json;

namespace Nox.McpServer.Tools;

[McpServerToolType]
public static class AgentTools
{
    [McpServerTool]
    [Description("List all agent templates available in the system")]
    public static async Task<string> ListAgentTemplates(IAgentTemplateRepository templateRepo)
    {
        var templates = await templateRepo.ListAllAsync();
        return JsonSerializer.Serialize(templates.Select(t => new
        {
            id = t.Id,
            name = t.Name,
            role = t.Role,
            description = t.Description,
            defaultModel = t.DefaultModel.ToString(),
            defaultMaxSubAgents = t.DefaultMaxSubAgents,
            isGlobal = t.IsGlobal
        }));
    }

    [McpServerTool]
    [Description("List all available skills (global, group, and personal)")]
    public static async Task<string> ListSkills(
        [Description("Optional agent ID to get effective skills for that agent")] string? agentId,
        ISkillRegistry skillRegistry)
    {
        List<Skill> skills;
        if (agentId is not null && Guid.TryParse(agentId, out var id))
            skills = await skillRegistry.GetEffectiveSkillsAsync(id);
        else
        {
            // Return all global skills
            skills = await skillRegistry.GetEffectiveSkillsAsync(Guid.Empty);
        }

        return JsonSerializer.Serialize(skills.Select(s => new
        {
            id = s.Id,
            slug = s.Slug,
            name = s.Name,
            description = s.Description,
            type = s.Type.ToString(),
            scope = s.Scope.ToString(),
            status = s.Status.ToString()
        }));
    }

    [McpServerTool]
    [Description("Get all slash commands available to an agent. Returns /<slug> commands.")]
    public static async Task<string> GetSlashCommands(
        [Description("Agent ID to get slash commands for")] string agentId,
        ISkillRegistry skillRegistry)
    {
        var commands = await skillRegistry.GetSlashCommandsAsync(Guid.Parse(agentId));
        return JsonSerializer.Serialize(commands.Select(c => new
        {
            command = $"/{c.Slug}",
            name = c.Name,
            description = c.Description
        }));
    }
}

// Repository interfaces (implemented in Infrastructure)
public interface IFlowRepository
{
    Task<List<Domain.Flows.Flow>> ListByProjectAsync(Guid projectId);
    Task<Domain.Flows.Flow?> FindByNameAsync(string name);
}

public interface IAgentTemplateRepository
{
    Task<List<AgentTemplate>> ListAllAsync();
}
