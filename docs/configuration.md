# Configuration

All secrets must be provided via environment variables. No hardcoded credentials exist in the codebase.

## Environment Variables (via `infra/.env`)

Copy `infra/.env.example` → `infra/.env` and fill in real values before starting.

| Variable | Description | Default (example) |
|----------|-------------|-------------------|
| `POSTGRES_PASSWORD` | PostgreSQL superuser password | — (required) |
| `REDIS_PASSWORD` | Redis `requirepass` password | — (required) |
| `QDRANT_API_KEY` | Qdrant REST API key | — (required) |
| `KEYCLOAK_ADMIN_PASSWORD` | Keycloak admin console password | — (required) |

## `appsettings.json` / `appsettings.Development.json`

> Secrets are read from env vars in `appsettings.json` via `${VAR_NAME}` syntax OR from the .NET Secret Manager in development. **Never commit real values.**

### Database

```json
"Nox": {
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=nox;Username=nox;Password=<POSTGRES_PASSWORD>"
  }
}
```

### Redis

```json
"Nox": {
  "Redis": {
    "ConnectionString": "localhost:6379,password=<REDIS_PASSWORD>"
  }
}
```

### Qdrant

```json
"Nox": {
  "Qdrant": {
    "Host": "localhost",
    "Port": "6334",
    "ApiKey": "<QDRANT_API_KEY>"
  }
}
```

### Orleans

```json
"Nox": {
  "Orleans": {
    "PostgresConnectionString": "Host=localhost;Port=5432;Database=nox_orleans;Username=nox;Password=<POSTGRES_PASSWORD>",
    "ClusterId": "nox-cluster",
    "ServiceId": "nox"
  }
}
```

### Auth (Keycloak)

```json
"Nox": {
  "Auth": {
    "Authority": "http://localhost:8080/realms/nox",
    "Audience": "nox-api",
    "RequireHttpsMetadata": "false"
  }
}
```

`RequireHttpsMetadata` should be `true` in production.

### LLM Providers

```json
"Nox": {
  "Llm": {
    "Providers": {
      "Anthropic": {
        "ApiKey": "<ANTHROPIC_API_KEY>"
      }
    }
  }
}
```

Models mapped:
- `LlmModel.Claude4` → `claude-opus-4-6`
- `LlmModel.Claude3Sonnet` → `claude-sonnet-4-6`

If a model is not configured, `Claude4` is used as fallback.

### CORS

```json
"Nox": {
  "Cors": {
    "AllowedOrigins": ["http://localhost:5050"]
  }
}
```

### Seq (observability)

```bash
SEQ_URL=http://localhost:5341
```

Set via environment variable. Defaults to `http://localhost:5341` if unset.

---

## Secret Management

### Development

Use .NET Secret Manager (not committed to git):

```bash
dotnet user-secrets set "Nox:Llm:Providers:Anthropic:ApiKey" "sk-ant-..."  \
  --project src/Nox.Api
```

### Production

Use your platform's secret manager (e.g. Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) and inject values as environment variables at runtime.

---

## Rate Limits

Configured in `Program.cs`:
- **300 requests/minute** per authenticated user (or per IP if anonymous)
- Response: `HTTP 429 Too Many Requests`

## Body Size Limit

- **1 MB** max request body (configured via Kestrel `MaxRequestBodySize`)
- Applies to all endpoints including `/acp/message`
