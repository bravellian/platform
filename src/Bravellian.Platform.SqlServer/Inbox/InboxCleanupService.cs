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


using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxCleanupService"/> class.
    /// </summary>
    /// <param name="options">Inbox cleanup options.</param>
    /// <param name="mono">Monotonic clock.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="schemaCompletion">Optional schema completion notifier.</param>
    public InboxCleanupService(
        IOptions<SqlInboxOptions> options,
        IMonotonicClock mono,
        ILogger<InboxCleanupService> logger,
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
            "Starting inbox cleanup service with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
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
                logger.LogWarning(ex, "Schema deployment failed, but continuing with inbox cleanup");
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
                    logger.LogInformation("Inbox cleanup completed: {DeletedCount} old messages deleted", deletedCount);
                }
                else
                {
                    logger.LogDebug("Inbox cleanup completed: no old messages to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Inbox cleanup service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let cleanup errors stop the service
                logger.LogError(ex, "Error during inbox cleanup - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Inbox cleanup service stopped");
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Uses fixed stored procedure name derived from validated schema and table names.")]
    private async Task<int> CleanupOldMessagesAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Starting inbox cleanup for messages older than {RetentionPeriod}", retentionPeriod);

        var sql = $"EXEC [{schemaName}].[{tableName}_Cleanup] @RetentionSeconds";

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RetentionSeconds", (int)retentionPeriod.TotalSeconds);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result, CultureInfo.InvariantCulture) : 0;
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            // Stored procedure doesn't exist yet - this is expected in multi-database setups
            // where databases may not be fully initialized
            logger.LogWarning(
                "Inbox cleanup stored procedure [{SchemaName}].[{TableName}_Cleanup] not found - skipping cleanup. " +
                "This is expected if the database schema hasn't been deployed yet.",
                schemaName,
                tableName);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup old inbox messages");
            throw;
        }
    }
}
