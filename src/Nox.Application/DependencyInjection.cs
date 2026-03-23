using Microsoft.Extensions.DependencyInjection;
using Nox.Application.Services;

namespace Nox.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNoxApplication(this IServiceCollection services)
    {
        services.AddScoped<IHitlApplicationService, HitlApplicationService>();
        services.AddScoped<IFlowApplicationService, FlowApplicationService>();
        services.AddScoped<ISkillApplicationService, SkillApplicationService>();
        return services;
    }
}
