using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nox.Domain.Flows;
using Nox.Domain.Mcp;
using Nox.Domain.Skills;
using Nox.Infrastructure;
using Nox.Infrastructure.Persistence;
using Nox.Orleans.Grains;
using Orleans.Configuration;

namespace Nox.Orleans;

public static class NoxSiloConfigurator
{
    public static IHostBuilder AddNoxOrleans(this IHostBuilder builder, IConfiguration configuration)
    {
        var orleansConnectionString = configuration["Nox:Orleans:PostgresConnectionString"]
            ?? "Host=localhost;Port=5433;Database=nox_orleans;Username=nox;Password=nox_secret";
        var redisConnectionString = configuration["Nox:Redis:ConnectionString"] ?? "localhost:6379";

        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = configuration["Nox:Orleans:ClusterId"] ?? "nox-cluster";
                    options.ServiceId = configuration["Nox:Orleans:ServiceId"] ?? "nox";
                })
                // Clustering via ADO.NET (PostgreSQL)
                .UseAdoNetClustering(options =>
                {
                    options.ConnectionString = orleansConnectionString;
                    options.Invariant = "Npgsql";
                })
                // Grain persistence via ADO.NET (PostgreSQL)
                .AddAdoNetGrainStorage("NoxStore", options =>
                {
                    options.ConnectionString = orleansConnectionString;
                    options.Invariant = "Npgsql";
                })
                // Reminders via Redis
                .UseRedisReminderService(options =>
                {
                    options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
                })
                // Silo endpoints
                .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000)
                // Register grain service dependencies
                .ConfigureServices(services =>
                {
                    services.AddScoped<IAgentTemplateResolver, DbAgentTemplateResolver>();
                    services.AddScoped<IFlowResolver, DbFlowResolver>();
                    // Full infrastructure needed by AgentGrain
                    services.AddNoxInfrastructure(configuration);
                });
        });

        return builder;
    }
}

// Resolvers that grains use to access DB entities
public class DbAgentTemplateResolver(NoxDbContext db) : IAgentTemplateResolver
{
    public async Task<Domain.Agents.AgentTemplate> ResolveAsync(Guid templateId)
    {
        return await db.AgentTemplates.FindAsync(templateId)
            ?? throw new KeyNotFoundException($"AgentTemplate {templateId} not found");
    }
}

public class DbFlowResolver(NoxDbContext db) : IFlowResolver
{
    public async Task<Flow> ResolveAsync(Guid flowId)
    {
        return await db.Flows.FindAsync(flowId)
            ?? throw new KeyNotFoundException($"Flow {flowId} not found");
    }
}
