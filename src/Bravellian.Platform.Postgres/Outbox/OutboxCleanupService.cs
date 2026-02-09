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


using Dapper;
using Npgsql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Background service that periodically cleans up old processed outbox messages
/// based on the configured retention period.
/// </summary>
public sealed class OutboxCleanupService : BackgroundService
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly ILogger<OutboxCleanupService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxCleanupService"/> class.
    /// </summary>
    /// <param name="options">Outbox cleanup options.</param>
    /// <param name="mono">Monotonic clock.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="schemaCompletion">Optional schema completion notifier.</param>
    public OutboxCleanupService(
        IOptions<PostgresOutboxOptions> options,
        IMonotonicClock mono,
        ILogger<OutboxCleanupService> logger,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mono);
        ArgumentNullException.ThrowIfNull(logger);

        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        retentionPeriod = opts.RetentionPeriod;
        cleanupInterval = opts.CleanupInterval;
        this.mono = mono;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
    }

    /// <summary>
    /// Runs the cleanup loop until cancellation.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>A task representing the background operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup loop logs failures and continues.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting outbox cleanup service with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
            retentionPeriod, cleanupInterval);

        // Wait for schema deployment to complete if available
        if (schemaCompletion != null)
        {
            logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent cleanup
                logger.LogWarning(ex, "Schema deployment failed, but continuing with outbox cleanup");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = mono.Seconds + cleanupInterval.TotalSeconds;

            try
            {
                var deletedCount = await CleanupOldMessagesAsync(stoppingToken).ConfigureAwait(false);
                if (deletedCount > 0)
                {
                    logger.LogInformation("Outbox cleanup completed: {DeletedCount} old messages deleted", deletedCount);
                }
                else
                {
                    logger.LogDebug("Outbox cleanup completed: no old messages to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Outbox cleanup service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let cleanup errors stop the service
                logger.LogError(ex, "Error during outbox cleanup - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Outbox cleanup service stopped");
    }

    private async Task<int> CleanupOldMessagesAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Starting outbox cleanup for messages older than {RetentionPeriod}", retentionPeriod);

        var qualifiedTable = PostgresSqlHelper.Qualify(schemaName, tableName);
        var sql = $"""
            WITH deleted AS (
                DELETE FROM {qualifiedTable}
                WHERE "Status" = 2
                    AND COALESCE("ProcessedOn", "ProcessedAt") IS NOT NULL
                    AND COALESCE("ProcessedOn", "ProcessedAt") < CURRENT_TIMESTAMP - (@RetentionSeconds || ' seconds')::interval
                RETURNING 1
            )
            SELECT COUNT(*) FROM deleted;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var result = await connection.ExecuteScalarAsync<int>(
                sql,
                new { RetentionSeconds = (int)retentionPeriod.TotalSeconds }).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup old outbox messages");
            throw;
        }
    }
}





