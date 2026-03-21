using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nox.Domain.Hitl;
using Nox.Domain.Llm;
using Nox.Domain.Memory;
using Nox.Domain.Mcp;
using Nox.Domain.Skills;
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
        services.AddDbContext<NoxDbContext>(options =>
            options.UseNpgsql(configuration["Nox:Database:ConnectionString"]
                ?? "Host=localhost;Port=5432;Database=nox;Username=nox;Password=nox_secret"));

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(
                configuration["Nox:Redis:ConnectionString"] ?? "localhost:6379"));

        // Qdrant
        services.AddSingleton(_ =>
        {
            var host = configuration["Nox:Qdrant:Host"] ?? "localhost";
            var port = int.Parse(configuration["Nox:Qdrant:Port"] ?? "6334");
            return new QdrantClient(host, port);
        });

        // Memory cache for skill registry
        services.AddMemoryCache();

        // Domain services
        services.AddScoped<IHitlQueue, PostgresHitlQueue>();
        services.AddScoped<ISkillRegistry, PostgresSkillRegistry>();
        services.AddScoped<IMemoryStore, QdrantMemoryStore>();
        services.AddSingleton<ILlmProvider, NoxLlmProvider>();
        services.AddScoped<IMcpClientManager, NoxMcpClientManager>();

        return services;
    }
}
