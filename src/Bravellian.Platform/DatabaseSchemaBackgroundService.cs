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
    private readonly IOptionsMonitor<SqlOutboxOptions> outboxOptions;
    private readonly IOptionsMonitor<SqlSchedulerOptions> schedulerOptions;
    private readonly IOptionsMonitor<SqlInboxOptions> inboxOptions;
    private readonly IOptionsMonitor<SemaphoreOptions> semaphoreOptions;
    private readonly DatabaseSchemaCompletion schemaCompletion;
    private readonly PlatformConfiguration platformConfiguration;
    private readonly IPlatformDatabaseDiscovery? databaseDiscovery;

    public DatabaseSchemaBackgroundService(
        ILogger<DatabaseSchemaBackgroundService> logger,
        IOptionsMonitor<SqlOutboxOptions> outboxOptions,
        IOptionsMonitor<SqlSchedulerOptions> schedulerOptions,
        IOptionsMonitor<SqlInboxOptions> inboxOptions,
        IOptionsMonitor<SemaphoreOptions> semaphoreOptions,
        DatabaseSchemaCompletion schemaCompletion,
        PlatformConfiguration platformConfiguration,
        IPlatformDatabaseDiscovery? databaseDiscovery = null)
    {
        this.logger = logger;
        this.outboxOptions = outboxOptions;
        this.schedulerOptions = schedulerOptions;
        this.inboxOptions = inboxOptions;
        this.semaphoreOptions = semaphoreOptions;
        this.schemaCompletion = schemaCompletion;
        this.platformConfiguration = platformConfiguration;
        this.databaseDiscovery = databaseDiscovery;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Starting database schema deployment");

            var deploymentTasks = new List<Task>();

            // Check if we're in a multi-database environment
            if (platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseNoControl ||
                platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl)
            {
                // Multi-database environment - deploy to all discovered databases
                if (platformConfiguration.EnableSchemaDeployment)
                {
                    deploymentTasks.Add(DeployMultiDatabaseSchemasAsync(stoppingToken));
                }

                // Deploy semaphore schema to control plane if configured
                if (platformConfiguration.EnableSchemaDeployment &&
                    platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                    !string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString))
                {
                    deploymentTasks.Add(DeploySemaphoreSchemaAsync(stoppingToken));
                    deploymentTasks.Add(DeployCentralMetricsSchemaAsync(stoppingToken));
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

                // Deploy semaphore schema if enabled
                if (platformConfiguration.EnableSchemaDeployment)
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

        var deploymentTasks = new List<Task>();

        // Deploy Outbox schema
        logger.LogDebug("Deploying outbox schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Outbox"));

        // Deploy Outbox work queue schema
        deploymentTasks.Add(DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            database.ConnectionString,
            database.SchemaName));

        // Deploy Inbox schema
        logger.LogDebug("Deploying inbox schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureInboxSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Inbox"));

        // Deploy Inbox work queue schema
        deploymentTasks.Add(DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            database.ConnectionString,
            database.SchemaName));

        // Deploy Scheduler schema (Jobs, JobRuns, Timers)
        logger.LogDebug("Deploying scheduler schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Jobs",
            "JobRuns",
            "Timers"));

        // Deploy Lease schema
        logger.LogDebug("Deploying lease schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureLeaseSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Lease"));

        // Deploy Fanout schema
        logger.LogDebug("Deploying fanout schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "FanoutPolicy",
            "FanoutCursor"));

        // Deploy Metrics schema
        logger.LogDebug("Deploying metrics schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureMetricsSchemaAsync(
            database.ConnectionString,
            "infra"));

        await Task.WhenAll(deploymentTasks).ConfigureAwait(false);

        logger.LogInformation(
            "Successfully deployed all platform schemas to database {DatabaseName}",
            database.Name);
    }

    private async Task DeployOutboxSchemaAsync(SqlOutboxOptions options, CancellationToken cancellationToken)
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

    private async Task DeploySchedulerSchemaAsync(SqlSchedulerOptions options, CancellationToken cancellationToken)
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

    private async Task DeployInboxSchemaAsync(SqlInboxOptions options, CancellationToken cancellationToken)
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

    private async Task DeploySemaphoreSchemaAsync(CancellationToken cancellationToken)
    {
        var options = semaphoreOptions.CurrentValue;
        logger.LogDebug("Deploying semaphore schema at {Schema}", options.SchemaName);
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeployCentralMetricsSchemaAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString))
        {
            logger.LogWarning("Central metrics schema deployment requested but no control plane connection string is configured");
            return;
        }

        logger.LogDebug("Deploying central metrics schema to control plane");
        await DatabaseSchemaManager.EnsureCentralMetricsSchemaAsync(
            platformConfiguration.ControlPlaneConnectionString,
            "infra").ConfigureAwait(false);
    }
}
