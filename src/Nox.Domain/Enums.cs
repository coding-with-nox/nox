namespace Nox.Domain;

public enum FlowStatus { Draft, Active, Paused, Completed, Failed }

public enum FlowRunStatus { Running, Paused, Completed, Failed, Cancelled }

public enum NodeType
{
    Start, End,
    AgentTask,
    HitlCheckpoint,
    Decision,
    Fork,
    Join
}

public enum AgentStatus { Idle, Running, WaitingForHitl, Suspended, Terminated }

public enum LlmModel
{
    Claude4,
    Claude3Sonnet,
    Gpt4o,
    Gpt4oMini,
    Gemini25Pro,
    Codex
}

public enum SkillType { SlashCommand, McpTool, Prompt, Workflow }

public enum SkillScope { Global, Group, Personal }

public enum SkillStatus { Active, PendingApproval, Rejected, Deprecated }

public enum CheckpointType { Approval, Review, DataInput, MultiChoice, Veto }

public enum CheckpointStatus { Pending, Approved, Rejected, Modified, Escalated, Expired }

public enum TaskStatus { Pending, Running, BlockedOnHitl, Completed, Failed, Cancelled }

public enum AcpMessageType { Request, Response, Event, Broadcast, Error }

public enum MemoryContentType { Code, Design, Decision, Error, Summary, Requirement }

public enum McpTransport { Stdio, Sse, Http }

public enum McpServerStatus { Active, Inactive, PendingApproval, Rejected }
