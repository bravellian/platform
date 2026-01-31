using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal static class SmokeServiceCollectionExtensions
{
    public static IServiceCollection AddSmokeServices(this IServiceCollection services)
    {
        services.AddSingleton<SmokeTestState>();
        services.AddSingleton<SmokeTestSignals>();
        services.AddSingleton<SmokeFanoutRepositories>();
        services.AddSingleton<SmokePlatformClientResolver>();
        services.AddSingleton<SmokeTestRunner>();

        services.AddSingleton<IFanoutDispatcher, SmokeFanoutDispatcher>();

        services.AddOutboxHandler<SmokeOutboxHandler>();
        services.AddOutboxHandler<SmokeSchedulerOutboxHandler>();
        services.AddOutboxHandler<SmokeFanoutSliceHandler>();
        services.AddInboxHandler<SmokeInboxHandler>();

        services.AddScoped<SmokeFanoutPlanner>();
        services.AddKeyedScoped<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey, (sp, _) => sp.GetRequiredService<SmokeFanoutPlanner>());
        services.AddKeyedScoped<IFanoutCoordinator>(SmokeFanoutDefaults.CoordinatorKey, (sp, _) =>
            new SmokeFanoutCoordinator(
                sp.GetRequiredKeyedService<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey),
                sp.GetRequiredService<IFanoutDispatcher>(),
                sp.GetRequiredService<ISystemLeaseFactory>(),
                sp.GetRequiredService<ILogger<SmokeFanoutCoordinator>>()));

        return services;
    }
}
