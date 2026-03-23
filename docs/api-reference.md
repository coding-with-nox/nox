# API Reference

Base URL: `http://localhost:5000` (dev) — all endpoints require a valid JWT (`Authorization: Bearer <token>`).

## Authentication

Obtain a token from Keycloak:

```bash
curl -s -X POST http://localhost:8080/realms/nox/protocol/openid-connect/token \
  -d "grant_type=password&client_id=nox-api&username=admin&password=admin" \
  | jq .access_token
```

---

## Flows

### `GET /api/flows`
List all flows.

**Query params**: `projectId` (UUID, optional filter)

**Response** `200`:
```json
[{ "id": "...", "name": "...", "status": "Active", "version": 1 }]
```

### `POST /api/flows`
Create a new flow.
**Role**: `manager` or `admin`

**Body**:
```json
{
  "name": "RequirementsAnalysis",
  "description": "...",
  "projectId": "00000000-0000-0000-0000-000000000001",
  "graph": {
    "nodes": [
      { "id": "start", "type": "Start" },
      { "id": "analyst", "type": "AgentTask", "agentTemplateId": "..." },
      { "id": "review", "type": "HitlCheckpoint", "title": "Review requirements" },
      { "id": "end", "type": "End" }
    ],
    "edges": [
      { "from": "start", "to": "analyst" },
      { "from": "analyst", "to": "review" },
      { "from": "review", "to": "end" }
    ]
  }
}
```

### `GET /api/flows/{id}/runs`
List all runs for a flow.

### `POST /api/flows/{id}/runs`
Start a new run.
**Role**: `manager` or `admin`

**Body**:
```json
{ "variables": { "projectName": "Acme CRM", "deadline": "2026-06-01" } }
```

**Response** `202`:
```json
{ "flowRunId": "...", "status": "Running" }
```

### `GET /api/flows/runs/{runId}`
Get run status and current node IDs.

---

## HITL Checkpoints

### `GET /api/hitl/pending`
List all pending checkpoints. Ordered by expiry then creation.

**Query params**: `skip`, `take` (pagination)

**Response** `200`:
```json
[{
  "id": "...",
  "flowRunId": "...",
  "type": "Approval",
  "title": "Review requirements document",
  "description": "...",
  "context": {},
  "createdAt": "2026-03-23T10:00:00Z",
  "expiresAt": "2026-03-24T10:00:00Z"
}]
```

### `POST /api/hitl/checkpoint/{id}/decide`
Submit a decision for a checkpoint.
**Role**: `manager` or `admin`

**Body**:
```json
{
  "decision": "Approved",
  "decidedBy": "tommaso",
  "payload": { "notes": "Looks good" }
}
```

`decision` values: `Approved` | `Rejected` | `Modified`

**Response** `200` | `404` | `409` (already resolved)

### `POST /api/hitl/checkpoint/{id}/escalate`
Escalate a checkpoint for further review.
**Role**: `manager` or `admin`

**Body**: `{ "reason": "Needs legal review" }`

---

## Agent Templates

### `GET /api/agents/templates`
List all agent templates.

### `POST /api/agents/templates`
Create an agent template.
**Role**: `admin`

**Body**:
```json
{
  "role": "RequirementsAnalyst",
  "defaultModel": "Claude3Sonnet",
  "systemPromptTemplate": "You are an expert requirements analyst...",
  "defaultMaxSubAgents": 3,
  "skillGroups": ["analysis", "documentation"],
  "tokenBudgetConfig": { "maxTokensPerTask": 80000 }
}
```

### `GET /api/flows/runs/{runId}/agents`
List agents active in a flow run.

---

## Skills

### `GET /api/skills`
List skills.

**Query params**: `scope` (`Global` | `Group` | `Personal`), `status` (`Active` | `PendingApproval`)

### `POST /api/skills`
Create a skill.
**Role**: `admin`

**Body**:
```json
{
  "slug": "run-tests",
  "type": "SlashCommand",
  "scope": "Global",
  "definition": { "command": "/test-runner", "description": "..." }
}
```

### `POST /api/skills/{id}/approve`
Approve a pending skill proposal.
**Role**: `admin`

### `POST /api/skills/{id}/reject`
Reject a pending skill proposal.
**Role**: `admin`

---

## MCP Servers

### `GET /api/mcp/servers`
List MCP servers. **Query params**: `status` (`Active` | `PendingApproval`)

### `GET /api/mcp/servers/{id}`
Get a single server.

### `GET /api/mcp/servers/{id}/tools`
Discover tools from a live MCP server (HTTP JSON-RPC).

### `POST /api/mcp/servers`
Register a server directly.
**Role**: `admin`

**Body**:
```json
{
  "id": "git-mcp",
  "name": "Git MCP",
  "transport": "Http",
  "endpointUrl": "https://git-mcp.internal/rpc",
  "status": "Active"
}
```

### `POST /api/mcp/servers/propose`
Agent proposes a new MCP server (creates HITL checkpoint for approval).

**Body**:
```json
{
  "agentId": "...",
  "name": "Browser MCP",
  "transport": "Http",
  "endpointUrl": "https://browser-mcp.internal/rpc",
  "justification": "Need web search capability"
}
```

### `POST /api/mcp/servers/{id}/approve`
Approve a proposed server.
**Role**: `admin`

**Body**: `{ "approvedBy": "tommaso" }`

### `POST /api/mcp/servers/{id}/invoke`
Invoke a tool on a server directly (for testing).
**Role**: `admin`

**Body**: `{ "toolName": "search", "args": { "query": "foo" } }`

---

## GDPR

### `POST /api/gdpr/anonymize/{username}`
Anonymize all PII for a user (GDPR Art. 17 Right to Erasure).
**Role**: `admin`

Replaces email, name, and `decidedBy` fields with `[anonymized]`. Records are kept; content is scrubbed.

---

## ACP (Agent Communication Protocol)

### `POST /acp/message`
Internal endpoint used by agents and grains to route messages. **Requires authentication.**

**Body** (`AcpMessage`):
```json
{
  "topic": "memory.store",
  "from": { "agentId": "..." },
  "correlationId": "...",
  "payload": { ... }
}
```

Supported topics: see `AcpTopics` constants in `Nox.Domain.Messages`.

---

## MCP Server Endpoint

### `GET|POST /mcp`
MCP SSE/HTTP transport endpoint. Used by external MCP clients (e.g. Claude Desktop).
**Requires authentication** (`NoxAnyUser`).

Available tools: `ListFlows`, `StartFlow`, `GetFlowStatus`, `ListPendingCheckpoints`, `SubmitCheckpointDecision`.

---

## Health

### `GET /health`
Returns `{ "status": "healthy", "timestamp": "..." }`. No auth required.

---

## SignalR Hubs

Requires valid JWT in connection handshake.

| Hub | URL | Events |
|-----|-----|--------|
| `HitlHub` | `/hubs/hitl` | `CheckpointCreated`, `CheckpointResolved` |
| `AgentMonitorHub` | `/hubs/agents` | `AgentStatusChanged`, `AgentTaskCompleted`, `TokenUsageUpdated` |

**Client subscription**:
```js
await connection.invoke("SubscribeToFlow", flowRunId);
```
