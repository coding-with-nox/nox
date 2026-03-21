using System.Text.Json.Nodes;

namespace Nox.Domain.Flows;

public record FlowPosition(double X, double Y);

public class FlowNode
{
    public required string Id { get; init; }
    public required NodeType NodeType { get; init; }
    public required string Label { get; init; }
    public Guid? AgentTemplateId { get; init; }
    public string? SkillRef { get; init; }
    public JsonObject Config { get; init; } = new();
    public FlowPosition Position { get; init; } = new(0, 0);
}

public class FlowEdge
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public string? Condition { get; init; }
}

public class FlowGraph
{
    public List<FlowNode> Nodes { get; init; } = [];
    public List<FlowEdge> Edges { get; init; } = [];

    public FlowNode? GetNode(string id) =>
        Nodes.FirstOrDefault(n => n.Id == id);

    public IEnumerable<FlowEdge> GetOutgoingEdges(string nodeId) =>
        Edges.Where(e => e.FromNodeId == nodeId);

    public IEnumerable<FlowEdge> GetIncomingEdges(string nodeId) =>
        Edges.Where(e => e.ToNodeId == nodeId);

    public FlowNode GetStartNode() =>
        Nodes.FirstOrDefault(n => n.NodeType == NodeType.Start)
            ?? throw new InvalidOperationException("Flow graph has no Start node");
}
