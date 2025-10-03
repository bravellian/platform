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
    private readonly DatabaseSchemaCompletion schemaCompletion;

    public DatabaseSchemaBackgroundService(
        ILogger<DatabaseSchemaBackgroundService> logger,
        IOptionsMonitor<SqlOutboxOptions> outboxOptions,
        IOptionsMonitor<SqlSchedulerOptions> schedulerOptions,
        IOptionsMonitor<SystemLeaseOptions> systemLeaseOptions,
        IOptionsMonitor<SqlInboxOptions> inboxOptions,
        DatabaseSchemaCompletion schemaCompletion)
    {
        this.logger = logger;
        this.outboxOptions = outboxOptions;
        this.schedulerOptions = schedulerOptions;
        this.systemLeaseOptions = systemLeaseOptions;
        this.inboxOptions = inboxOptions;
        this.schemaCompletion = schemaCompletion;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            this.logger.LogInformation("Starting database schema deployment");

            var deploymentTasks = new List<Task>();

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
            options.SchemaName, options.JobsTableName, options.JobRunsTableName, options.TimersTableName);
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
}
