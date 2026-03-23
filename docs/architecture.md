# Architecture

## Overview

Nox is a **multi-agent orchestration system** where AI agents simulate roles in a software house (Analyst, Architect, Developer, QA, etc.). A human operator (the HITL) reviews and approves agent actions at configured checkpoints.

## Component Diagram

```
┌────────────────────────────────────────────────────────────────┐
│                        Nox.Api (ASP.NET Core 10)               │
│                                                                  │
│  ┌─────────────────┐  ┌──────────────┐  ┌───────────────────┐  │
│  │  REST Controllers│  │ SignalR Hubs │  │  MCP SSE /mcp     │  │
│  │  /api/*          │  │ /hubs/hitl  │  │  (FlowTools etc.) │  │
│  │                  │  │ /hubs/agents│  │                   │  │
│  └────────┬─────────┘  └──────┬───────┘  └────────┬──────────┘  │
│           │                   │                    │             │
│  ┌────────▼─────────────────────────────────────────▼──────────┐ │
│  │              Orleans Silo (co-hosted)                        │ │
│  │                                                              │ │
│  │   FlowGrain ──► AgentGrain ──► AgentGrain (sub-agents)      │ │
│  │       │              │                                       │ │
│  │       │         tool calls                                   │ │
│  │       │         ┌────┴────────────────────────────┐         │ │
│  │       │         │ ISkillRegistry  IMcpClientManager│         │ │
│  │       │         │ IMemoryStore    ILlmProvider     │         │ │
│  │       │         └────────────────────────────────┘│         │ │
│  │       │                                            │         │ │
│  │       ▼                                            │         │ │
│  │  IHitlQueue ◄──────────────────────────────────────┘         │ │
│  └──────────────────────────────────────────────────────────────┘ │
└──────────┬──────────────────────┬───────────────────┬─────────────┘
           │                      │                   │
     ┌─────▼──────┐        ┌──────▼──────┐     ┌─────▼──────┐
     │ PostgreSQL  │        │    Redis 7  │     │   Qdrant   │
     │ EF Core 10  │        │ pub/sub+KV  │     │  vectors   │
     └─────────────┘        └─────────────┘     └────────────┘

External:
  Keycloak 26 ── JWT Bearer auth ──► Nox.Api
  Seq ──────────────────────────────◄ Serilog
  Nox.Dashboard (Blazor Server) ─── SignalR ──► /hubs/*
```

## Projects

| Project | Responsibility |
|---------|---------------|
| `Nox.Domain` | Pure domain models and interfaces. No framework dependencies. |
| `Nox.Infrastructure` | EF Core, Redis, Qdrant, LLM adapters, MCP client, GDPR service. |
| `Nox.Orleans` | Grain definitions (`FlowGrain`, `AgentGrain`). Silo configuration. |
| `Nox.McpServer` | Exposes Nox as an MCP server: `FlowTools`, `AgentTools`, `SkillTools`. |
| `Nox.Api` | ASP.NET Core host: REST, SignalR, ACP middleware, auth pipeline. |
| `Nox.Dashboard` | Blazor Server: HITL queue, flow designer, agent monitor. |

## Key Domain Concepts

### Flow

A directed graph of nodes (`FlowGraph`). Node types:

| Type | Behavior |
|------|----------|
| `Start` | Entry point; initializes variables |
| `AgentTask` | Assigns a task to an `AgentGrain` |
| `HitlCheckpoint` | Pauses execution until human decision |
| `Decision` | Evaluates a CEL expression, follows one branch |
| `Fork` | Starts parallel branches (child `FlowGrain` per branch) |
| `Join` | Waits for all branches; aggregates results |
| `End` | Marks run as completed |

Edges carry optional `Condition` (CEL expression evaluated at runtime).

### AgentGrain (Orleans)

Stateful grain that implements the **think-tool-observe loop**:

```
InitializeAsync
  → resolve effective skills (Personal > Group > Global)
  → connect MCP servers from template
  → load top-K memory chunks

ExecuteTaskAsync loop
  → build system prompt (template + skills + memory)
  → call IChatClient.GetResponseAsync()
  → dispatch tool calls:
      slash command  → ISkillRegistry
      MCP tool       → IMcpClientManager
      spawn-subagent → SpawnSubAgentAsync (respects MaxSubAgents)
      hitl-request   → IHitlQueue
  → check token budget → SummarizeAndPruneAsync if near limit
  → no tool call → extract TaskResult → write to IMemoryStore
```

### FlowGrain (Orleans)

Implements `AdvanceAsync(completedNodeId, result)`:

1. Persist result in `FlowState.NodeResults`
2. Evaluate edge conditions to find next nodes
3. Activate next node (spawn agent / enqueue HITL / fork / join)

**HITL pause**: FlowGrain sets an Orleans reminder before pausing. When `HitlController.DecideAsync` is called, it calls `IFlowGrain.ResumeFromCheckpointAsync(id, decision)` which cancels the reminder and calls `AdvanceAsync`.

### Memory (3 Layers)

| Layer | Store | Scope | ~Tokens |
|-------|-------|-------|---------|
| Working memory | In-grain `List<ChatMessage>` | Current turn | 8K |
| Agent personal | Qdrant + PG (`project_memory` where `agent_id IS NOT NULL`) | Agent-specific | 10K |
| Project shared | Qdrant + PG (`project_memory` where `agent_id IS NULL`) | All project agents | 20K |

Pruning: `SummarizeAndPruneAsync` calls the LLM to summarize low-importance chunks, then deletes originals from both Qdrant and PG.

### ACP — Agent Communication Protocol

All inter-agent messages go through the orchestrator (no peer-to-peer). Route: `POST /acp/message` → `AcpRoutingMiddleware` → appropriate service.

**Topics** (constants in `AcpTopics`):

```
task.assigned / task.result / task.progress
agent.spawn.request / agent.spawn.response
skill.propose / skill.approved
hitl.request / hitl.decision
memory.store / memory.query.request / memory.query.response
mcp.server.request
broadcast
```

## Data Flow: Flow Execution

```
POST /api/flows/{id}/runs
  → FlowsController.StartRunAsync
  → IFlowEngine.StartAsync
  → FlowGrain.StartAsync (grain activated)
  → FlowGrain advances to first AgentTask
  → AgentGrain.ExecuteTaskAsync (LLM loop)
  → AgentGrain calls tool → result
  → AgentGrain signals FlowGrain (task complete)
  → FlowGrain.AdvanceAsync
  → next node: HitlCheckpoint
  → IHitlQueue.EnqueueAsync → PostgreSQL + Redis publish
  → SignalR (RedisSignalRBridge) → Dashboard HitlReviewQueue
  → Human clicks Approve
  → POST /api/hitl/checkpoint/{id}/decide
  → IFlowGrain.ResumeFromCheckpointAsync
  → FlowGrain.AdvanceAsync → End → FlowRun.Status = Completed
```

## Orleans Configuration

- **Clustering**: ADO.NET (PostgreSQL) — no Kubernetes/ZooKeeper required
- **Grain persistence**: ADO.NET (`NoxStore`) — grain state survives restarts
- **Reminders**: Redis — survives silo restarts, used for HITL timeout
- **Silo ports**: 11111 (silo-to-silo), 30000 (gateway)
- **Co-hosting**: Silo runs inside the same process as ASP.NET Core (`builder.Host.AddNoxOrleans(...)`)

## Authentication & Authorization

See [security.md](security.md) for full details.

- **Provider**: Keycloak 26 (JWT Bearer)
- **Claim mapping**: `KeycloakRolesTransformer` maps `realm_access.roles` → `ClaimTypes.Role`
- **Policies**: `NoxAnyUser` (Viewer+), `NoxManagerOrAdmin`, `NoxAdminOnly`
- **All endpoints** require at least `NoxAnyUser`; write/admin operations require elevated roles
