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

using Bravellian.Platform.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for PostgreSQL email delivery logging.
/// </summary>
internal static class PostgresEmailDeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL email delivery logging with the specified options.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="options">Email delivery options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPostgresEmailDelivery(
        this IServiceCollection services,
        PostgresEmailDeliveryOptions options)
    {
        var validator = new PostgresEmailDeliveryOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresEmailDeliveryOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresEmailDeliveryOptions>>(validator));

        services.Configure<PostgresEmailDeliveryOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddTimeAbstractions();
        services.TryAddSingleton<IEmailDeliverySink, PostgresEmailDeliverySink>();

        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL email delivery logging using a connection string.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="schemaName">Schema name (default: "infra").</param>
    /// <param name="tableName">Table name (default: "EmailDeliveryEvents").</param>
    /// <param name="enableSchemaDeployment">Whether schema deployment should run at startup.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPostgresEmailDelivery(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "infra",
        string tableName = "EmailDeliveryEvents",
        bool enableSchemaDeployment = false)
    {
        return services.AddPostgresEmailDelivery(new PostgresEmailDeliveryOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName,
            EnableSchemaDeployment = enableSchemaDeployment,
        });
    }
}
