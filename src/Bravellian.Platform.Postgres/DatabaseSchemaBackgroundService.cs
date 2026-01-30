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


using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Background service that handles database schema deployment and signals completion to dependent services.
/// </summary>
internal sealed class DatabaseSchemaBackgroundService : BackgroundService
{
    private readonly ILogger<DatabaseSchemaBackgroundService> logger;
    private readonly IOptionsMonitor<PostgresOutboxOptions> outboxOptions;
    private readonly IOptionsMonitor<PostgresSchedulerOptions> schedulerOptions;
    private readonly IOptionsMonitor<PostgresInboxOptions> inboxOptions;
    private readonly IOptionsMonitor<PostgresIdempotencyOptions> idempotencyOptions;
    private readonly IOptionsMonitor<PostgresOperationOptions> operationOptions;
    private readonly IOptionsMonitor<PostgresAuditOptions> auditOptions;
    private readonly IOptionsMonitor<PostgresEmailOutboxOptions> emailOutboxOptions;
    private readonly IOptionsMonitor<PostgresEmailDeliveryOptions> emailDeliveryOptions;
    private readonly IOptionsMonitor<PostgresSemaphoreOptions> semaphoreOptions;
    private readonly IOptionsMonitor<PostgresSystemLeaseOptions> systemLeaseOptions;
    private readonly DatabaseSchemaCompletion schemaCompletion;
    private readonly PlatformConfiguration? platformConfiguration;
    private readonly IPlatformDatabaseDiscovery? databaseDiscovery;
    private readonly IStartupLatch? startupLatch;

    public DatabaseSchemaBackgroundService(
        ILogger<DatabaseSchemaBackgroundService> logger,
        IOptionsMonitor<PostgresOutboxOptions> outboxOptions,
        IOptionsMonitor<PostgresSchedulerOptions> schedulerOptions,
        IOptionsMonitor<PostgresInboxOptions> inboxOptions,
        IOptionsMonitor<PostgresIdempotencyOptions> idempotencyOptions,
        IOptionsMonitor<PostgresOperationOptions> operationOptions,
        IOptionsMonitor<PostgresAuditOptions> auditOptions,
        IOptionsMonitor<PostgresEmailOutboxOptions> emailOutboxOptions,
        IOptionsMonitor<PostgresEmailDeliveryOptions> emailDeliveryOptions,
        IOptionsMonitor<PostgresSemaphoreOptions> semaphoreOptions,
        IOptionsMonitor<PostgresSystemLeaseOptions> systemLeaseOptions,
        DatabaseSchemaCompletion schemaCompletion,
        PlatformConfiguration? platformConfiguration = null,
        IPlatformDatabaseDiscovery? databaseDiscovery = null,
        IStartupLatch? startupLatch = null)
    {
        this.logger = logger;
        this.outboxOptions = outboxOptions;
        this.schedulerOptions = schedulerOptions;
        this.inboxOptions = inboxOptions;
        this.idempotencyOptions = idempotencyOptions;
        this.operationOptions = operationOptions;
        this.auditOptions = auditOptions;
        this.emailOutboxOptions = emailOutboxOptions;
        this.emailDeliveryOptions = emailDeliveryOptions;
        this.semaphoreOptions = semaphoreOptions;
        this.systemLeaseOptions = systemLeaseOptions;
        this.schemaCompletion = schemaCompletion;
        this.platformConfiguration = platformConfiguration;
        this.databaseDiscovery = databaseDiscovery;
        this.startupLatch = startupLatch;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var step = startupLatch?.Register("platform-migrations");

        try
        {
            logger.LogInformation("Starting database schema deployment");

            var deploymentTasks = new List<Task>();

            // Check if we're in a multi-database environment
            var isMultiDatabase = platformConfiguration is not null &&
                (platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseNoControl ||
                 platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl);

            if (isMultiDatabase)
            {
                // Multi-database environment - deploy to all discovered databases
                if (platformConfiguration!.EnableSchemaDeployment)
                {
                    deploymentTasks.Add(DeployMultiDatabaseSchemasAsync(stoppingToken));
                }

                // Deploy semaphore schema to control plane if configured
                if (platformConfiguration!.EnableSchemaDeployment &&
                    platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                    !string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString))
                {
                    deploymentTasks.Add(DeployControlPlaneBundlesAsync(stoppingToken));
                }
            }
            else
            {
                // Single database environment - use the original logic
                // Deploy outbox schema if enabled
                var outboxOpts = outboxOptions.CurrentValue;
                if (outboxOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(outboxOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployOutboxSchemaAsync(outboxOpts, stoppingToken));
                }

                // Deploy scheduler schema if enabled
                var schedulerOpts = schedulerOptions.CurrentValue;
                if (schedulerOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(schedulerOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeploySchedulerSchemaAsync(schedulerOpts, stoppingToken));
                }

                // Deploy inbox schema if enabled
                var inboxOpts = inboxOptions.CurrentValue;
                if (inboxOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(inboxOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployInboxSchemaAsync(inboxOpts, stoppingToken));
                }

                var idempotencyOpts = idempotencyOptions.CurrentValue;
                if (idempotencyOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(idempotencyOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployIdempotencySchemaAsync(idempotencyOpts, stoppingToken));
                }

                var operationsOpts = operationOptions.CurrentValue;
                if (operationsOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(operationsOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployOperationsSchemaAsync(operationsOpts, stoppingToken));
                }

                var auditOpts = auditOptions.CurrentValue;
                if (auditOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(auditOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployAuditSchemaAsync(auditOpts, stoppingToken));
                }

                var emailOutboxOpts = emailOutboxOptions.CurrentValue;
                if (emailOutboxOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(emailOutboxOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployEmailOutboxSchemaAsync(emailOutboxOpts, stoppingToken));
                }

                var emailDeliveryOpts = emailDeliveryOptions.CurrentValue;
                if (emailDeliveryOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(emailDeliveryOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeployEmailDeliverySchemaAsync(emailDeliveryOpts, stoppingToken));
                }

                // Deploy system lease schema if enabled
                var systemLeaseOpts = systemLeaseOptions.CurrentValue;
                if (systemLeaseOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(systemLeaseOpts.ConnectionString))
                {
                    deploymentTasks.Add(DeploySystemLeaseSchemaAsync(systemLeaseOpts, stoppingToken));
                }

                // Deploy semaphore schema if enabled
                if (platformConfiguration?.EnableSchemaDeployment == true)
                {
                    deploymentTasks.Add(DeploySemaphoreSchemaAsync(stoppingToken));
                }
            }

            if (deploymentTasks.Count > 0)
            {
                await Task.WhenAll(deploymentTasks).ConfigureAwait(false);
                logger.LogInformation("Database schema deployment completed successfully");
            }
            else
            {
                logger.LogInformation("No schema deployments configured - skipping schema deployment");
            }

            // Signal completion to dependent services
            schemaCompletion.SetCompleted();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Database schema deployment was cancelled");
            schemaCompletion.SetCancelled(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database schema deployment failed");
            schemaCompletion.SetException(ex);
            throw; // Re-throw to stop the host if schema deployment fails
        }
    }

    private async Task DeployMultiDatabaseSchemasAsync(CancellationToken cancellationToken)
    {
        if (databaseDiscovery == null)
        {
            logger.LogWarning("Multi-database schema deployment requested but no database discovery service is available");
            return;
        }

        logger.LogInformation("Discovering databases for schema deployment");
        var databases = await databaseDiscovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);

        if (databases.Count == 0)
        {
            logger.LogWarning("No databases discovered for schema deployment");
            return;
        }

        logger.LogInformation("Deploying schemas to {DatabaseCount} database(s)", databases.Count);

        var tasks = new List<Task>();
        foreach (var database in databases)
        {
            tasks.Add(DeploySchemasToDatabaseAsync(database, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DeploySchemasToDatabaseAsync(PlatformDatabase database, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Deploying platform schemas to database {DatabaseName} (Schema: {SchemaName})",
            database.Name,
            database.SchemaName);

        logger.LogDebug("Deploying tenant bundle to database {DatabaseName}", database.Name);
        await DatabaseSchemaManager.ApplyTenantBundleAsync(
            database.ConnectionString,
            database.SchemaName).ConfigureAwait(false);

        logger.LogInformation(
            "Successfully deployed all platform schemas to database {DatabaseName}",
            database.Name);
    }

    private async Task DeployOutboxSchemaAsync(PostgresOutboxOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug("Deploying outbox schema to {Schema}.{Table}", options.SchemaName, options.TableName);
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.TableName).ConfigureAwait(false);

        // Also deploy work queue schema for outbox
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeploySchedulerSchemaAsync(PostgresSchedulerOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Deploying scheduler schema to {Schema} with tables {Jobs}, {JobRuns}, {Timers}",
            options.SchemaName,
            options.JobsTableName,
            options.JobRunsTableName,
            options.TimersTableName);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.JobsTableName,
            options.JobRunsTableName,
            options.TimersTableName).ConfigureAwait(false);
    }

    private async Task DeployInboxSchemaAsync(PostgresInboxOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug("Deploying inbox schema to {Schema}.{Table}", options.SchemaName, options.TableName);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.TableName).ConfigureAwait(false);

        // Also deploy inbox work queue schema for dispatcher
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeployIdempotencySchemaAsync(PostgresIdempotencyOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug("Deploying idempotency schema to {Schema}.{Table}", options.SchemaName, options.TableName);
        await DatabaseSchemaManager.EnsureIdempotencySchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.TableName).ConfigureAwait(false);
    }

    private async Task DeployOperationsSchemaAsync(PostgresOperationOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Deploying operations schema to {Schema}.{OperationsTable}",
            options.SchemaName,
            options.OperationsTable);
        await DatabaseSchemaManager.EnsureOperationsSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.OperationsTable,
            options.OperationEventsTable).ConfigureAwait(false);
    }

    private async Task DeployAuditSchemaAsync(PostgresAuditOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Deploying audit schema to {Schema}.{AuditEventsTable}",
            options.SchemaName,
            options.AuditEventsTable);
        await DatabaseSchemaManager.EnsureAuditSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.AuditEventsTable,
            options.AuditAnchorsTable).ConfigureAwait(false);
    }

    private async Task DeployEmailOutboxSchemaAsync(PostgresEmailOutboxOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Deploying email outbox schema to {Schema}.{Table}",
            options.SchemaName,
            options.TableName);
        await DatabaseSchemaManager.EnsureEmailOutboxSchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.TableName).ConfigureAwait(false);
    }

    private async Task DeployEmailDeliverySchemaAsync(PostgresEmailDeliveryOptions options, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Deploying email delivery schema to {Schema}.{Table}",
            options.SchemaName,
            options.TableName);
        await DatabaseSchemaManager.EnsureEmailDeliverySchemaAsync(
            options.ConnectionString,
            options.SchemaName,
            options.TableName).ConfigureAwait(false);
    }

    private async Task DeploySemaphoreSchemaAsync(CancellationToken cancellationToken)
    {
        var options = semaphoreOptions.CurrentValue;
        logger.LogDebug("Deploying semaphore schema at {Schema}", options.SchemaName);
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeploySystemLeaseSchemaAsync(PostgresSystemLeaseOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("ConnectionString must be provided.", nameof(options));
        }

        logger.LogDebug("Deploying system lease schema at {Schema}", options.SchemaName);
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeployControlPlaneBundlesAsync(CancellationToken cancellationToken)
    {
        if (platformConfiguration is null || string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString))
        {
            return;
        }

        var schemaName = string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneSchemaName)
            ? "infra"
            : platformConfiguration.ControlPlaneSchemaName;

        logger.LogDebug("Deploying tenant bundle to control plane");
        await DatabaseSchemaManager.ApplyTenantBundleAsync(
            platformConfiguration.ControlPlaneConnectionString,
            schemaName).ConfigureAwait(false);

        logger.LogDebug("Deploying control-plane bundle");
        await DatabaseSchemaManager.ApplyControlPlaneBundleAsync(
            platformConfiguration.ControlPlaneConnectionString,
            schemaName).ConfigureAwait(false);
    }
}





