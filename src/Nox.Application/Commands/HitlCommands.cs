using System.Text.Json.Nodes;

namespace Nox.Application.Commands;

/// <summary>Submit a HITL decision. decidedBy must come from the authenticated identity, never from user input.</summary>
public record SubmitHitlDecisionCommand(
    Guid CheckpointId,
    string Decision,
    JsonObject? Payload,
    string DecidedBy);

public record EscalateCheckpointCommand(
    Guid CheckpointId,
    string Reason);
