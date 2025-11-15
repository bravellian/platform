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

/// <summary>
/// Background service that periodically cleans up old processed inbox messages
/// from multiple databases based on the configured retention period.
/// </summary>
internal sealed class MultiInboxCleanupService : BackgroundService
{
    private readonly IInboxWorkStoreProvider storeProvider;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly ILogger<MultiInboxCleanupService> logger;

    public MultiInboxCleanupService(
        IInboxWorkStoreProvider storeProvider,
        IMonotonicClock mono,
        ILogger<MultiInboxCleanupService> logger,
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
        this.logger.LogInformation(
            "Starting multi-inbox cleanup service with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
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
                var totalDeleted = await this.CleanupAllDatabasesAsync(stoppingToken).ConfigureAwait(false);
                if (totalDeleted > 0)
                {
                    this.logger.LogInformation("Multi-inbox cleanup completed: {DeletedCount} old messages deleted across all databases", totalDeleted);
                }
                else
                {
                    this.logger.LogDebug("Multi-inbox cleanup completed: no old messages to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                this.logger.LogDebug("Multi-inbox cleanup service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let cleanup errors stop the service
                this.logger.LogError(ex, "Error during multi-inbox cleanup - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - this.mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        this.logger.LogInformation("Multi-inbox cleanup service stopped");
    }

    private async Task<int> CleanupAllDatabasesAsync(CancellationToken cancellationToken)
    {
        var stores = this.storeProvider.GetAllStores();
        var totalDeleted = 0;

        foreach (var store in stores)
        {
            try
            {
                var identifier = this.storeProvider.GetStoreIdentifier(store);
                this.logger.LogDebug("Starting inbox cleanup for database: {DatabaseIdentifier}", identifier);

                var deleted = await this.CleanupDatabaseAsync(store, identifier, cancellationToken).ConfigureAwait(false);
                totalDeleted += deleted;

                if (deleted > 0)
                {
                    this.logger.LogDebug("Deleted {DeletedCount} old messages from database: {DatabaseIdentifier}", deleted, identifier);
                }
            }
            catch (Exception ex)
            {
                var identifier = this.storeProvider.GetStoreIdentifier(store);
                this.logger.LogError(ex, "Failed to cleanup old inbox messages from database: {DatabaseIdentifier}", identifier);
                // Continue with other databases
            }
        }

        return totalDeleted;
    }

    private async Task<int> CleanupDatabaseAsync(IInboxWorkStore store, string identifier, CancellationToken cancellationToken)
    {
        // Extract connection details from the store
        // We need to get the connection string, schema name, and table name
        // These are available from SqlInboxWorkStore via reflection
        var storeType = store.GetType();
        
        if (storeType.Name != "SqlInboxWorkStore")
        {
            this.logger.LogWarning("Skipping cleanup for non-SQL store: {StoreType}", storeType.Name);
            return 0;
        }

        // Get options field using reflection
        var optionsField = storeType.GetField("options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (optionsField == null)
        {
            this.logger.LogWarning("Could not access options field for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var optionsValue = optionsField.GetValue(store);
        if (optionsValue == null)
        {
            this.logger.LogWarning("Options field is null for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var optionsType = optionsValue.GetType();
        var connectionStringProp = optionsType.GetProperty("ConnectionString");
        var schemaNameProp = optionsType.GetProperty("SchemaName");
        var tableNameProp = optionsType.GetProperty("TableName");

        if (connectionStringProp == null || schemaNameProp == null || tableNameProp == null)
        {
            this.logger.LogWarning("Could not access options properties for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var connectionString = connectionStringProp.GetValue(optionsValue) as string;
        var schemaName = schemaNameProp.GetValue(optionsValue) as string;
        var tableName = tableNameProp.GetValue(optionsValue) as string;

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(tableName))
        {
            this.logger.LogWarning("Invalid options values for store: {DatabaseIdentifier}", identifier);
            return 0;
        }

        var sql = $"EXEC [{schemaName}].[{tableName}_Cleanup] @RetentionSeconds";

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RetentionSeconds", (int)this.retentionPeriod.TotalSeconds);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to execute cleanup stored procedure for database: {DatabaseIdentifier}", identifier);
            throw;
        }
    }
}
