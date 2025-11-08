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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that periodically cleans up old processed inbox messages
/// based on the configured retention period.
/// </summary>
public sealed class InboxCleanupService : BackgroundService
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly ILogger<InboxCleanupService> logger;

    public InboxCleanupService(
        IOptions<SqlInboxOptions> options,
        IMonotonicClock mono,
        ILogger<InboxCleanupService> logger,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        var opts = options.Value;
        this.connectionString = opts.ConnectionString;
        this.schemaName = opts.SchemaName;
        this.tableName = opts.TableName;
        this.retentionPeriod = opts.RetentionPeriod;
        this.cleanupInterval = opts.CleanupInterval;
        this.mono = mono;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation(
            "Starting inbox cleanup service with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
            this.retentionPeriod, this.cleanupInterval);

        // Wait for schema deployment to complete if available
        if (this.schemaCompletion != null)
        {
            this.logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await this.schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                this.logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent cleanup
                this.logger.LogWarning(ex, "Schema deployment failed, but continuing with inbox cleanup");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = this.mono.Seconds + this.cleanupInterval.TotalSeconds;

            try
            {
                var deletedCount = await this.CleanupOldMessagesAsync(stoppingToken).ConfigureAwait(false);
                if (deletedCount > 0)
                {
                    this.logger.LogInformation("Inbox cleanup completed: {DeletedCount} old messages deleted", deletedCount);
                }
                else
                {
                    this.logger.LogDebug("Inbox cleanup completed: no old messages to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                this.logger.LogDebug("Inbox cleanup service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let cleanup errors stop the service
                this.logger.LogError(ex, "Error during inbox cleanup - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - this.mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        this.logger.LogInformation("Inbox cleanup service stopped");
    }

    private async Task<int> CleanupOldMessagesAsync(CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Starting inbox cleanup for messages older than {RetentionPeriod}", this.retentionPeriod);

        var sql = $"EXEC [{this.schemaName}].[{this.tableName}_Cleanup] @RetentionSeconds";

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RetentionSeconds", (int)this.retentionPeriod.TotalSeconds);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to cleanup old inbox messages");
            throw;
        }
    }
}
