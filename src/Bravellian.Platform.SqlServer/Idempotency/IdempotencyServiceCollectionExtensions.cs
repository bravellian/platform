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

using Bravellian.Platform.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for SQL Server idempotency stores.
/// </summary>
internal static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server idempotency tracking with the specified options.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="options">Idempotency options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSqlIdempotency(
        this IServiceCollection services,
        SqlIdempotencyOptions options)
    {
        var validator = new SqlIdempotencyOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<SqlIdempotencyOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SqlIdempotencyOptions>>(validator));

        services.Configure<SqlIdempotencyOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.LockDuration = options.LockDuration;
            o.LockDurationProvider = options.LockDurationProvider;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddTimeAbstractions();
        services.TryAddSingleton<IIdempotencyStoreProvider>(sp =>
        {
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new ConfiguredIdempotencyStoreProvider(new[] { options }, timeProvider, loggerFactory);
        });
        services.TryAddSingleton<IIdempotencyStoreRouter, IdempotencyStoreRouter>();
        services.TryAddSingleton<IIdempotencyStore>(ResolveDefaultIdempotencyStore);

        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds SQL Server idempotency tracking with custom schema and table names.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <param name="schemaName">Schema name.</param>
    /// <param name="tableName">Table name.</param>
    /// <param name="lockDuration">Lock duration.</param>
    /// <param name="enableSchemaDeployment">Whether schema deployment should run at startup.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSqlIdempotency(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "infra",
        string tableName = "Idempotency",
        TimeSpan? lockDuration = null,
        Func<string, TimeSpan>? lockDurationProvider = null,
        bool enableSchemaDeployment = false)
    {
        return services.AddSqlIdempotency(new SqlIdempotencyOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName,
            LockDuration = lockDuration ?? TimeSpan.FromMinutes(5),
            LockDurationProvider = lockDurationProvider,
            EnableSchemaDeployment = enableSchemaDeployment,
        });
    }

    private static IIdempotencyStore ResolveDefaultIdempotencyStore(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<IIdempotencyStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No idempotency stores are configured. Configure at least one store or use IIdempotencyStoreRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple idempotency stores are configured. Resolve IIdempotencyStoreRouter instead of IIdempotencyStore for multi-database setups.");
        }

        var router = provider.GetRequiredService<IIdempotencyStoreRouter>();
        var key = storeProvider.GetStoreIdentifier(stores[0]);
        return router.GetStore(key);
    }
}
