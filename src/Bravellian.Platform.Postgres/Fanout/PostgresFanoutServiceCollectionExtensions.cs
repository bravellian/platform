// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Extension methods for configuring fanout services with the DI container.
/// </summary>
internal static class PostgresFanoutServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL multi-fanout functionality with support for processing across multiple databases.
    /// This enables a single worker to process fanout operations from multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="fanoutOptions">List of fanout options, one for each database.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiPostgresFanout(
        this IServiceCollection services,
        IEnumerable<PostgresFanoutOptions> fanoutOptions)
    {
        ArgumentNullException.ThrowIfNull(fanoutOptions);

        var optionsList = fanoutOptions.ToList();
        if (optionsList.Count == 0)
        {
            throw new ArgumentException("At least one fanout option must be provided.", nameof(fanoutOptions));
        }

        var validator = new PostgresFanoutOptionsValidator();
        foreach (var option in optionsList)
        {
            OptionsValidationHelper.ValidateAndThrow(option, validator);
        }

        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the repository provider with the list of fanout options
        services.AddSingleton<IFanoutRepositoryProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredFanoutRepositoryProvider(optionsList, loggerFactory);
        });

        // Register the fanout router for routing operations to the correct database
        services.AddSingleton<IFanoutRouter, FanoutRouter>();

        // Register schema deployment service if any options have it enabled
        if (optionsList.Any(o => o.EnableSchemaDeployment))
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());

            // Only add hosted service if not already registered using TryAddEnumerable
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds SQL multi-fanout functionality using a custom repository provider.
    /// This allows for dynamic discovery of fanout databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="repositoryProviderFactory">Factory function to create the repository provider.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    internal static IServiceCollection AddMultiPostgresFanout(
        this IServiceCollection services,
        Func<IServiceProvider, IFanoutRepositoryProvider> repositoryProviderFactory)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the custom repository provider
        services.AddSingleton(repositoryProviderFactory);

        // Register the fanout router for routing operations to the correct database
        services.AddSingleton<IFanoutRouter, FanoutRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-fanout functionality with dynamic database discovery.
    /// This enables automatic detection of new or removed customer databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <remarks>
    /// Requires an implementation of IFanoutDatabaseDiscovery to be registered in the service collection.
    /// The discovery service is responsible for querying a registry, database, or configuration service
    /// to get the current list of customer databases.
    /// </remarks>
    public static IServiceCollection AddDynamicMultiSqlFanout(
        this IServiceCollection services,
        TimeSpan? refreshInterval = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the dynamic repository provider
        services.AddSingleton<IFanoutRepositoryProvider>(provider =>
        {
            var discovery = provider.GetRequiredService<IFanoutDatabaseDiscovery>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<DynamicFanoutRepositoryProvider>>();
            return new DynamicFanoutRepositoryProvider(discovery, timeProvider, loggerFactory, logger, refreshInterval);
        });

        // Register the fanout router for routing operations to the correct database
        services.AddSingleton<IFanoutRouter, FanoutRouter>();

        return services;
    }
}





