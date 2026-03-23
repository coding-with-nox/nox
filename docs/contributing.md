# Contributing

## Conventions

### Code Style

- **C# 13** with file-scoped namespaces, primary constructors, collection expressions
- `var` everywhere inference is obvious
- Async methods suffixed with `Async`
- No `async void` (except event handlers)
- Inject via constructor (primary constructor pattern used throughout)

### Project Boundaries

| Layer | Can reference |
|-------|--------------|
| `Nox.Domain` | nothing (pure) |
| `Nox.Infrastructure` | `Nox.Domain` |
| `Nox.Orleans` | `Nox.Domain`, `Nox.Infrastructure` |
| `Nox.McpServer` | `Nox.Domain` |
| `Nox.Api` | all above |
| `Nox.Dashboard` | `Nox.Domain` (via HTTP, not project ref) |

Do not add circular references. Do not add framework dependencies to `Nox.Domain`.

### Adding a New Endpoint

1. Add controller in `src/Nox.Api/Controllers/`
2. Annotate class with `[Authorize(Policy = NoxPolicies.AnyUser)]` (minimum)
3. Use `[Authorize(Policy = NoxPolicies.ManagerOrAdmin)]` for write operations
4. Return `IActionResult` — use `Ok()`, `NotFound()`, `CreatedAtAction()`
5. Never expose raw `ex.Message` in responses

### Adding a New Grain

1. Define interface in `src/Nox.Orleans/GrainInterfaces/`
2. Implement in `src/Nox.Orleans/Grains/`
3. Register any DI dependencies in `NoxSiloConfigurator.ConfigureServices`
4. Use `[StorageProvider(ProviderName = "NoxStore")]` for grain persistence

### Adding a New Domain Model

1. Add entity in `src/Nox.Domain/`
2. Add `DbSet<T>` to `NoxDbContext`
3. Configure via `modelBuilder` in `NoxDbContext.OnModelCreating`
4. Generate migration: `dotnet ef migrations add <Name> --project src/Nox.Infrastructure --startup-project src/Nox.Api`

### Adding a New MCP Tool

1. Add static method to an existing `[McpServerToolType]` class or create a new one in `src/Nox.McpServer/Tools/`
2. Annotate with `[McpServerTool]` and `[Description("...")]`
3. Inject domain services via method parameters (ModelContextProtocol DI)
4. Always use `IHttpContextAccessor` to identify the caller — never accept caller identity from tool arguments

## Security Checklist (per PR)

- [ ] No hardcoded secrets (passwords, API keys)
- [ ] New HTTP endpoints have `[Authorize]`
- [ ] No raw `ex.Message` in HTTP responses
- [ ] External URLs are validated via `ValidateEndpointUrl()` (SSRF)
- [ ] New MCP tools do not accept `decidedBy` / identity from arguments
- [ ] New DB queries that update status use atomic `WHERE status = X` filter

## Branching

```
main          ← stable, deployable
feature/xyz   ← feature work
fix/xyz       ← bug fixes
```

PRs require passing `dotnet build` (0 errors). Warnings are acceptable but should not grow.

## Testing

```bash
# Unit tests
dotnet test tests/Nox.Domain.Tests

# Orleans grain tests (TestCluster)
dotnet test tests/Nox.Orleans.Tests

# API integration tests
dotnet test tests/Nox.Api.Tests
```

Tests targeting grains use `Microsoft.Orleans.TestingHost` (`TestCluster`) — no real silo needed.
