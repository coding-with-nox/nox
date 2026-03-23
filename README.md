# Nox — HITL Multi-Agent Orchestration System

> Orchestrate specialized AI agents across a software-development lifecycle with human-in-the-loop checkpoints, persistent memory, and MCP tool integration.

## Quick Links

| Doc | Description |
|-----|-------------|
| [Architecture](docs/architecture.md) | System design, component diagram, data flow |
| [API Reference](docs/api-reference.md) | REST endpoints, request/response schemas |
| [Configuration](docs/configuration.md) | Environment variables, appsettings, secrets |
| [Security](docs/security.md) | RBAC, GDPR, EU AI Act, pen-test fixes |
| [Development Setup](docs/dev-setup.md) | Prerequisites, docker compose, run locally |
| [Contributing](docs/contributing.md) | Conventions, branching, testing |

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Orchestration runtime | Microsoft Orleans 10 (ADO.NET / PostgreSQL) |
| API | ASP.NET Core 10 (REST + SignalR + MCP SSE) |
| LLM abstraction | `Microsoft.Extensions.AI` (`IChatClient`) |
| MCP | `ModelContextProtocol` 1.1 (official SDK) |
| Vector DB | Qdrant (Docker) |
| Relational DB | PostgreSQL 16 (EF Core 10) |
| Pub/Sub + KV | Redis 7 |
| Dashboard | Blazor Server |
| Auth | Keycloak 26 (OAuth2/OIDC, JWT Bearer) |
| Observability | Serilog → Seq |

## Fast Start

```bash
cp infra/.env.example infra/.env   # fill in real secrets
cd infra && docker compose up -d
dotnet run --project src/Nox.Api
# browse http://localhost:5000/swagger
```

See [dev-setup.md](docs/dev-setup.md) for details.
