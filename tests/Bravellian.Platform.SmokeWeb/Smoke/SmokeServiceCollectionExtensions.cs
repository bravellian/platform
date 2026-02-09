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
        services.AddOutboxHandler(sp => new SmokeFanoutSliceHandler(
            sp.GetRequiredService<SmokeTestState>(),
            sp.GetRequiredService<SmokeTestSignals>(),
            sp.GetRequiredService<SmokeFanoutRepositories>(),
            sp.GetRequiredService<TimeProvider>(),
            SmokeFanoutDefaults.SliceTopic(SmokeFanoutDefaults.WorkKey)));
        services.AddOutboxHandler(sp => new SmokeFanoutSliceHandler(
            sp.GetRequiredService<SmokeTestState>(),
            sp.GetRequiredService<SmokeTestSignals>(),
            sp.GetRequiredService<SmokeFanoutRepositories>(),
            sp.GetRequiredService<TimeProvider>(),
            SmokeFanoutDefaults.SliceTopic(SmokeFanoutDefaults.WorkKeyBurst)));
        services.AddInboxHandler<SmokeInboxHandler>();

        services.AddScoped<SmokeFanoutPlanner>();
        services.AddKeyedScoped<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKey), (sp, _) => sp.GetRequiredService<SmokeFanoutPlanner>());
        services.AddKeyedScoped<IFanoutCoordinator>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKey), (sp, _) =>
            new SmokeFanoutCoordinator(
                sp.GetRequiredKeyedService<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKey)),
                sp.GetRequiredService<IFanoutDispatcher>(),
                sp.GetRequiredService<ISystemLeaseFactory>(),
                sp.GetRequiredService<ILogger<SmokeFanoutCoordinator>>()));
        services.AddKeyedScoped<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKeyBurst), (sp, _) => sp.GetRequiredService<SmokeFanoutPlanner>());
        services.AddKeyedScoped<IFanoutCoordinator>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKeyBurst), (sp, _) =>
            new SmokeFanoutCoordinator(
                sp.GetRequiredKeyedService<IFanoutPlanner>(SmokeFanoutDefaults.CoordinatorKey(SmokeFanoutDefaults.WorkKeyBurst)),
                sp.GetRequiredService<IFanoutDispatcher>(),
                sp.GetRequiredService<ISystemLeaseFactory>(),
                sp.GetRequiredService<ILogger<SmokeFanoutCoordinator>>()));

        return services;
    }
}
