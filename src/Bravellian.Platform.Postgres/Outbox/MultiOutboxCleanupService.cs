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

namespace Bravellian.Platform;
/// <summary>
/// Background service that periodically cleans up old processed outbox messages
/// from multiple databases based on the configured retention period.
/// </summary>
internal sealed class MultiOutboxCleanupService : BackgroundService
{
    private readonly IOutboxStoreProvider storeProvider;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly ILogger<MultiOutboxCleanupService> logger;

    public MultiOutboxCleanupService(
        IOutboxStoreProvider storeProvider,
        IMonotonicClock mono,
        ILogger<MultiOutboxCleanupService> logger,
        TimeSpan? retentionPeriod = null,
        TimeSpan? cleanupInterval = null,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.storeProvider = storeProvider;
        this.retentionPeriod = retentionPeriod ?? TimeSpan.FromDays(7);
        this.cleanupInterval = cleanupInterval ?? TimeSpan.FromHours(1);
        this.mono = mono;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting multi-outbox cleanup service with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
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
                var totalDeleted = await CleanupAllDatabasesAsync(stoppingToken).ConfigureAwait(false);
                if (totalDeleted > 0)
                {
                    logger.LogInformation("Multi-outbox cleanup completed: {DeletedCount} old messages deleted across all databases", totalDeleted);
                }
                else
                {
                    logger.LogDebug("Multi-outbox cleanup completed: no old messages to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Multi-outbox cleanup service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let cleanup errors stop the service
                logger.LogError(ex, "Error during multi-outbox cleanup - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Multi-outbox cleanup service stopped");
    }

    private async Task<int> CleanupAllDatabasesAsync(CancellationToken cancellationToken)
    {
        var stores = await storeProvider.GetAllStoresAsync().ConfigureAwait(false);
        var totalDeleted = 0;

        foreach (var store in stores)
        {
            try
            {
                var identifier = storeProvider.GetStoreIdentifier(store);
                logger.LogDebug("Starting outbox cleanup for database: {DatabaseIdentifier}", identifier);

                var deleted = await CleanupDatabaseAsync(store, identifier, cancellationToken).ConfigureAwait(false);
                totalDeleted += deleted;

                if (deleted > 0)
                {
                    logger.LogDebug("Deleted {DeletedCount} old messages from database: {DatabaseIdentifier}", deleted, identifier);
                }
            }
            catch (Exception ex)
            {
                var identifier = storeProvider.GetStoreIdentifier(store);
                logger.LogError(ex, "Failed to cleanup old outbox messages from database: {DatabaseIdentifier}", identifier);
                // Continue with other databases
            }
        }

        return totalDeleted;
    }

    private async Task<int> CleanupDatabaseAsync(IOutboxStore store, string identifier, CancellationToken cancellationToken)
    {
        // Extract connection details from the store
        // We need to get the connection string, schema name, and table name
        // These are stored as private readonly fields in PostgresOutboxStore
        var storeType = store.GetType();

        if (!string.Equals(storeType.Name, "PostgresOutboxStore", StringComparison.Ordinal))
        {
            logger.LogWarning("Skipping cleanup for non-SQL store: {StoreType}", storeType.Name);
            return 0;
        }

        // Get private readonly fields using reflection
        var connectionStringField = storeType.GetField("connectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var schemaNameField = storeType.GetField("schemaName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var tableNameField = storeType.GetField("tableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (connectionStringField == null || schemaNameField == null || tableNameField == null)
        {
            logger.LogWarning("Could not access required fields for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var connectionString = connectionStringField.GetValue(store) as string;
        var schemaName = schemaNameField.GetValue(store) as string;
        var tableName = tableNameField.GetValue(store) as string;

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(tableName))
        {
            logger.LogWarning("Invalid field values for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var qualifiedTable = PostgresSqlHelper.Qualify(schemaName, tableName);
        var sql = $"""
            WITH deleted AS (
                DELETE FROM {qualifiedTable}
                WHERE "Status" = 2
                    AND "ProcessedAt" IS NOT NULL
                    AND "ProcessedAt" < CURRENT_TIMESTAMP - (@RetentionSeconds || ' seconds')::interval
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
            logger.LogError(ex, "Failed to execute cleanup for database: {DatabaseIdentifier}", identifier);
            throw;
        }
    }
}





