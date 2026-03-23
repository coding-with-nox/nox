# Development Setup

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| Docker Desktop | 4.x+ |
| `dotnet ef` CLI | `dotnet tool install -g dotnet-ef` |

## 1. Clone & configure secrets

```bash
git clone <repo-url>
cd hitl-multi-agents-orchestration

cp infra/.env.example infra/.env
# Edit infra/.env with real passwords / API keys
```

## 2. Start infrastructure

```bash
cd infra
docker compose up -d
# Starts: PostgreSQL, Redis, Qdrant, Keycloak, Seq
```

Wait ~15 seconds for Keycloak to finish importing the `nox` realm.

Verify:
- PostgreSQL: `docker compose ps` → all healthy
- Keycloak admin: http://localhost:8080 (user: `admin`, password: from `.env`)
- Seq: http://localhost:5341

## 3. Configure user secrets

```bash
# From repo root
dotnet user-secrets set "Nox:Database:ConnectionString" \
  "Host=localhost;Port=5432;Database=nox;Username=nox;Password=<YOUR_POSTGRES_PASSWORD>" \
  --project src/Nox.Api

dotnet user-secrets set "Nox:Redis:ConnectionString" \
  "localhost:6379,password=<YOUR_REDIS_PASSWORD>" \
  --project src/Nox.Api

dotnet user-secrets set "Nox:Qdrant:ApiKey" "<YOUR_QDRANT_API_KEY>" \
  --project src/Nox.Api

dotnet user-secrets set "Nox:Orleans:PostgresConnectionString" \
  "Host=localhost;Port=5432;Database=nox_orleans;Username=nox;Password=<YOUR_POSTGRES_PASSWORD>" \
  --project src/Nox.Api

dotnet user-secrets set "Nox:Llm:Providers:Anthropic:ApiKey" "sk-ant-..." \
  --project src/Nox.Api
```

## 4. Run database migrations

```bash
dotnet ef database update --project src/Nox.Infrastructure --startup-project src/Nox.Api
```

If this is a fresh database, EF will create all tables automatically on first run via `MigrateAsync()` in `Program.cs`.

## 5. Run the API

```bash
dotnet run --project src/Nox.Api
```

- Swagger: http://localhost:5000/swagger
- Health: http://localhost:5000/health
- MCP: http://localhost:5000/mcp

## 6. Run the Dashboard

```bash
dotnet run --project src/Nox.Dashboard
# Browse http://localhost:5050
```

## 7. Obtain a dev token

Dev users (created by Keycloak realm import):

| Username | Password | Roles |
|----------|----------|-------|
| `admin` | `admin123` | `admin`, `manager`, `viewer` |
| `manager` | `manager123` | `manager`, `viewer` |
| `viewer` | `viewer123` | `viewer` |

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/nox/protocol/openid-connect/token \
  -d "grant_type=password&client_id=nox-api&username=admin&password=admin123" \
  | jq -r .access_token)

# Test
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/flows
```

## 8. Seed example data (optional)

```bash
# POST a simple flow via Swagger or curl
curl -s -X POST http://localhost:5000/api/flows \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d @infra/seed/sample-flow.json
```

## Running Tests

```bash
dotnet test
```

## Common Issues

### Orleans fails to start

Check `Nox:Orleans:PostgresConnectionString` is set and the `nox_orleans` database exists. The Orleans ADO.NET schema is auto-created on first silo start.

### Keycloak 401 on all requests

Verify `Nox:Auth:Authority` matches the actual realm URL. In Docker, use `http://localhost:8080/realms/nox` (not the container hostname).

### Qdrant connection refused

Check `Nox:Qdrant:Host` and `Port` (default `6334` for gRPC). The collection is auto-created by `QdrantMemoryStore` on first write.

## Generate Self-Signed TLS Cert (dev only)

```bash
bash infra/certs/generate-dev-certs.sh
# Produces infra/certs/nox-dev.crt + nox-dev.key
```

Trust the cert in your OS keystore for browser access without warnings.
