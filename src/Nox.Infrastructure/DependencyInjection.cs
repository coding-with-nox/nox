using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nox.Domain.Hitl;
using Nox.Domain.Llm;
using Nox.Domain.Memory;
using Nox.Domain.Mcp;
using Nox.Domain.Skills;
using Nox.Domain.Gdpr;
using Nox.Infrastructure.Gdpr;
using Nox.Infrastructure.Hitl;
using Nox.Infrastructure.Llm;
using Nox.Infrastructure.Memory;
using Nox.Infrastructure.Mcp;
using Nox.Infrastructure.Persistence;
using Nox.Infrastructure.Skills;
using Qdrant.Client;
using StackExchange.Redis;

namespace Nox.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNoxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // PostgreSQL / EF Core
        var dbConnStr = configuration["Nox:Database:ConnectionString"]
            ?? throw new InvalidOperationException("Nox:Database:ConnectionString is not configured. Set it via appsettings.Development.json or environment variable.");
        services.AddDbContext<NoxDbContext>(options => options.UseNpgsql(dbConnStr));
        services.AddDbContextFactory<NoxDbContext>(options => options.UseNpgsql(dbConnStr), ServiceLifetime.Scoped);

        // Redis
        var redisConnStr = configuration["Nox:Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Nox:Redis:ConnectionString is not configured.");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnStr));

        // Qdrant
        services.AddSingleton(_ =>
        {
            var host = configuration["Nox:Qdrant:Host"] ?? "localhost";
            var port = int.Parse(configuration["Nox:Qdrant:Port"] ?? "6334");
            var apiKey = configuration["Nox:Qdrant:ApiKey"];
            return apiKey is not null
                ? new QdrantClient(host, port, apiKey: apiKey)
                : new QdrantClient(host, port);
        });

        // Memory cache for skill registry
        services.AddMemoryCache();

        // HTTP client for MCP servers
        services.AddHttpClient("McpClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // HTTP client + handler for GitHub API
        services.AddHttpClient("GitHub", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.BaseAddress = new Uri("https://api.github.com");
        });
        services.AddScoped<Nox.Infrastructure.GitHub.GitHubToolHandler>();

        // Domain services
        services.AddScoped<IHitlQueue, PostgresHitlQueue>();
        services.AddScoped<ISkillRegistry, PostgresSkillRegistry>();
        services.AddScoped<IMemoryStore, QdrantMemoryStore>();
        services.AddSingleton<ILlmProvider, NoxLlmProvider>();
        services.AddScoped<IMcpClientManager, NoxMcpClientManager>();

        // GDPR
        services.AddScoped<IGdprService, GdprService>();

        return services;
    }
}
