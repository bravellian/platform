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

namespace Bravellian.Platform;

using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Background service that handles database schema deployment and signals completion to dependent services.
/// </summary>
internal sealed class DatabaseSchemaBackgroundService : BackgroundService
{
    private readonly ILogger<DatabaseSchemaBackgroundService> logger;
    private readonly IOptionsMonitor<SqlOutboxOptions> outboxOptions;
    private readonly IOptionsMonitor<SqlSchedulerOptions> schedulerOptions;
    private readonly IOptionsMonitor<SystemLeaseOptions> systemLeaseOptions;
    private readonly IOptionsMonitor<SqlInboxOptions> inboxOptions;
    private readonly IOptionsMonitor<SemaphoreOptions> semaphoreOptions;
    private readonly DatabaseSchemaCompletion schemaCompletion;
    private readonly PlatformConfiguration platformConfiguration;
    private readonly IPlatformDatabaseDiscovery? databaseDiscovery;

    public DatabaseSchemaBackgroundService(
        ILogger<DatabaseSchemaBackgroundService> logger,
        IOptionsMonitor<SqlOutboxOptions> outboxOptions,
        IOptionsMonitor<SqlSchedulerOptions> schedulerOptions,
        IOptionsMonitor<SystemLeaseOptions> systemLeaseOptions,
        IOptionsMonitor<SqlInboxOptions> inboxOptions,
        IOptionsMonitor<SemaphoreOptions> semaphoreOptions,
        DatabaseSchemaCompletion schemaCompletion,
        PlatformConfiguration platformConfiguration,
        IPlatformDatabaseDiscovery? databaseDiscovery = null)
    {
        this.logger = logger;
        this.outboxOptions = outboxOptions;
        this.schedulerOptions = schedulerOptions;
        this.systemLeaseOptions = systemLeaseOptions;
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
            this.logger.LogInformation("Starting database schema deployment");

            var deploymentTasks = new List<Task>();

            // Check if we're in a multi-database environment
            if (this.platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseNoControl ||
                this.platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl)
            {
                // Multi-database environment - deploy to all discovered databases
                if (this.platformConfiguration.EnableSchemaDeployment)
                {
                    deploymentTasks.Add(this.DeployMultiDatabaseSchemasAsync(stoppingToken));
                }

                // Deploy semaphore schema to control plane if configured
                if (this.platformConfiguration.EnableSchemaDeployment &&
                    this.platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                    !string.IsNullOrEmpty(this.platformConfiguration.ControlPlaneConnectionString))
                {
                    deploymentTasks.Add(this.DeploySemaphoreSchemaAsync(stoppingToken));
                    deploymentTasks.Add(this.DeployCentralMetricsSchemaAsync(stoppingToken));
                }
            }
            else
            {
                // Single database environment - use the original logic
                // Deploy outbox schema if enabled
                var outboxOpts = this.outboxOptions.CurrentValue;
                if (outboxOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(outboxOpts.ConnectionString))
                {
                    deploymentTasks.Add(this.DeployOutboxSchemaAsync(outboxOpts, stoppingToken));
                }

                // Deploy scheduler schema if enabled
                var schedulerOpts = this.schedulerOptions.CurrentValue;
                if (schedulerOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(schedulerOpts.ConnectionString))
                {
                    deploymentTasks.Add(this.DeploySchedulerSchemaAsync(schedulerOpts, stoppingToken));
                }

                // Deploy system lease schema if enabled
                var systemLeaseOpts = this.systemLeaseOptions.CurrentValue;
                if (systemLeaseOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(systemLeaseOpts.ConnectionString))
                {
                    deploymentTasks.Add(this.DeploySystemLeaseSchemaAsync(systemLeaseOpts, stoppingToken));
                }

                // Deploy inbox schema if enabled
                var inboxOpts = this.inboxOptions.CurrentValue;
                if (inboxOpts.EnableSchemaDeployment && !string.IsNullOrEmpty(inboxOpts.ConnectionString))
                {
                    deploymentTasks.Add(this.DeployInboxSchemaAsync(inboxOpts, stoppingToken));
                }

                // Deploy semaphore schema if enabled
                if (this.platformConfiguration.EnableSchemaDeployment)
                {
                    deploymentTasks.Add(this.DeploySemaphoreSchemaAsync(stoppingToken));
                }
            }

            if (deploymentTasks.Count > 0)
            {
                await Task.WhenAll(deploymentTasks).ConfigureAwait(false);
                this.logger.LogInformation("Database schema deployment completed successfully");
            }
            else
            {
                this.logger.LogInformation("No schema deployments configured - skipping schema deployment");
            }

            // Signal completion to dependent services
            this.schemaCompletion.SetCompleted();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            this.logger.LogInformation("Database schema deployment was cancelled");
            this.schemaCompletion.SetCancelled(stoppingToken);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Database schema deployment failed");
            this.schemaCompletion.SetException(ex);
            throw; // Re-throw to stop the host if schema deployment fails
        }
    }

    private async Task DeployMultiDatabaseSchemasAsync(CancellationToken cancellationToken)
    {
        if (this.databaseDiscovery == null)
        {
            this.logger.LogWarning("Multi-database schema deployment requested but no database discovery service is available");
            return;
        }

        this.logger.LogInformation("Discovering databases for schema deployment");
        var databases = await this.databaseDiscovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);

        if (databases.Count == 0)
        {
            this.logger.LogWarning("No databases discovered for schema deployment");
            return;
        }

        this.logger.LogInformation("Deploying schemas to {DatabaseCount} database(s)", databases.Count);

        var tasks = new List<Task>();
        foreach (var database in databases)
        {
            tasks.Add(this.DeploySchemasToDatabaseAsync(database, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DeploySchemasToDatabaseAsync(PlatformDatabase database, CancellationToken cancellationToken)
    {
        this.logger.LogInformation(
            "Deploying platform schemas to database {DatabaseName} (Schema: {SchemaName})",
            database.Name,
            database.SchemaName);

        var deploymentTasks = new List<Task>();

        // Deploy Outbox schema
        this.logger.LogDebug("Deploying outbox schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Outbox"));

        // Deploy Outbox work queue schema
        deploymentTasks.Add(DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            database.ConnectionString,
            database.SchemaName));

        // Deploy Inbox schema
        this.logger.LogDebug("Deploying inbox schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureInboxSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Inbox"));

        // Deploy Inbox work queue schema
        deploymentTasks.Add(DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            database.ConnectionString,
            database.SchemaName));

        // Deploy Scheduler schema (Jobs, JobRuns, Timers)
        this.logger.LogDebug("Deploying scheduler schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Jobs",
            "JobRuns",
            "Timers"));

        // Deploy Lease schema
        this.logger.LogDebug("Deploying lease schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureLeaseSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "Lease"));

        // Deploy Fanout schema
        this.logger.LogDebug("Deploying fanout schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            database.ConnectionString,
            database.SchemaName,
            "FanoutPolicy",
            "FanoutCursor"));

        // Deploy Metrics schema
        this.logger.LogDebug("Deploying metrics schema to database {DatabaseName}", database.Name);
        deploymentTasks.Add(DatabaseSchemaManager.EnsureMetricsSchemaAsync(
            database.ConnectionString,
            "infra"));

        await Task.WhenAll(deploymentTasks).ConfigureAwait(false);

        this.logger.LogInformation(
            "Successfully deployed all platform schemas to database {DatabaseName}",
            database.Name);
    }

    private async Task DeployOutboxSchemaAsync(SqlOutboxOptions options, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Deploying outbox schema to {Schema}.{Table}", options.SchemaName, options.TableName);
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
        this.logger.LogDebug(
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

    private async Task DeploySystemLeaseSchemaAsync(SystemLeaseOptions options, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Deploying system lease schema to {Schema}", options.SchemaName);
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeployInboxSchemaAsync(SqlInboxOptions options, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Deploying inbox schema to {Schema}.{Table}", options.SchemaName, options.TableName);
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
        var options = this.semaphoreOptions.CurrentValue;
        this.logger.LogDebug("Deploying semaphore schema at {Schema}", options.SchemaName);
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(
            options.ConnectionString,
            options.SchemaName).ConfigureAwait(false);
    }

    private async Task DeployCentralMetricsSchemaAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(this.platformConfiguration.ControlPlaneConnectionString))
        {
            this.logger.LogWarning("Central metrics schema deployment requested but no control plane connection string is configured");
            return;
        }

        this.logger.LogDebug("Deploying central metrics schema to control plane");
        await DatabaseSchemaManager.EnsureCentralMetricsSchemaAsync(
            this.platformConfiguration.ControlPlaneConnectionString,
            "infra").ConfigureAwait(false);
    }
}
