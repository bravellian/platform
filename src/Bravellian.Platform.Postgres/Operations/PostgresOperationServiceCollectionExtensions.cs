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

using Bravellian.Platform.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for PostgreSQL operation tracking.
/// </summary>
internal static class PostgresOperationServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL operation tracking using the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresOperations(
        this IServiceCollection services,
        PostgresOperationOptions options)
    {
        var validator = new PostgresOperationOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresOperationOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresOperationOptions>>(validator));

        services.Configure<PostgresOperationOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.OperationsTable = options.OperationsTable;
            o.OperationEventsTable = options.OperationEventsTable;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddTimeAbstractions();
        services.TryAddSingleton<IOperationTracker, PostgresOperationTracker>();
        services.TryAddSingleton<IOperationWatcher, PostgresOperationWatcher>();

        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL operation tracking using a connection string and optional schema/table settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="schemaName">Schema name (default: "infra").</param>
    /// <param name="operationsTable">Operations table name (default: "Operations").</param>
    /// <param name="operationEventsTable">Operation events table name (default: "OperationEvents").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresOperations(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "infra",
        string operationsTable = "Operations",
        string operationEventsTable = "OperationEvents")
    {
        return services.AddPostgresOperations(new PostgresOperationOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            OperationsTable = operationsTable,
            OperationEventsTable = operationEventsTable,
        });
    }
}
