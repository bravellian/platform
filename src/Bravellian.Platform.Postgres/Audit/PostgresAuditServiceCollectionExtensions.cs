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

using Bravellian.Platform.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for Postgres audit storage.
/// </summary>
public static class PostgresAuditServiceCollectionExtensions
{
    /// <summary>
    /// Adds Postgres audit storage using the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Audit options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPostgresAudit(
        this IServiceCollection services,
        PostgresAuditOptions options)
    {
        var validator = new PostgresAuditOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresAuditOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresAuditOptions>>(validator));

        services.Configure<PostgresAuditOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.AuditEventsTable = options.AuditEventsTable;
            o.AuditAnchorsTable = options.AuditAnchorsTable;
            o.ValidationOptions = options.ValidationOptions;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.TryAddSingleton<IAuditEventWriter, PostgresAuditEventWriter>();
        services.TryAddSingleton<IAuditEventReader, PostgresAuditEventReader>();

        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds Postgres audit storage using the specified connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="schemaName">Schema name (default: "infra").</param>
    /// <param name="auditEventsTable">Audit events table name (default: "AuditEvents").</param>
    /// <param name="auditAnchorsTable">Audit anchors table name (default: "AuditAnchors").</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPostgresAudit(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "infra",
        string auditEventsTable = "AuditEvents",
        string auditAnchorsTable = "AuditAnchors")
    {
        return services.AddPostgresAudit(new PostgresAuditOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            AuditEventsTable = auditEventsTable,
            AuditAnchorsTable = auditAnchorsTable,
        });
    }
}
