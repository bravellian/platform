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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// SQL Server implementation of IOutboxStore using optimized queries with proper locking hints.
/// </summary>
internal class SqlOutboxStore : IOutboxStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlOutboxStore> logger;
    private readonly string serverName;
    private readonly string databaseName;

    public SqlOutboxStore(IOptions<SqlOutboxOptions> options, TimeProvider timeProvider, ILogger<SqlOutboxStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;

        (serverName, databaseName) = ParseConnectionInfo(connectionString);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        logger.LogDebug("Claiming up to {Limit} outbox messages for processing", limit);

        // Use READPAST to skip locked rows, UPDLOCK to prevent other readers from claiming same rows
        var sql = $"""

                        SELECT TOP ({limit}) * 
                        FROM [{schemaName}].[{tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
                        WHERE IsProcessed = 0 
                          AND NextAttemptAt <= SYSDATETIMEOFFSET()
                          AND (DueTimeUtc IS NULL OR CAST(DueTimeUtc AS DATETIMEOFFSET) <= SYSDATETIMEOFFSET())
                        ORDER BY CreatedAt
            """;

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var messages = await connection.QueryAsync<OutboxMessage>(sql).ConfigureAwait(false);
            var messageList = messages.ToList();

            logger.LogDebug("Successfully claimed {ClaimedCount} outbox messages for processing", messageList.Count);
            return messageList;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to claim outbox messages from store {Schema}.{Table} on {Server}/{Database}",
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task MarkDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogDebug("Marking outbox message {MessageId} as dispatched", id);

        var sql = $"""

                        UPDATE [{schemaName}].[{tableName}]
                        SET IsProcessed = 1, 
                            ProcessedAt = SYSDATETIMEOFFSET(),
                            ProcessedBy = @ProcessedBy
                        WHERE Id = @Id
            """;

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                ProcessedBy = Environment.MachineName,
            }).ConfigureAwait(false);

            logger.LogDebug("Successfully marked outbox message {MessageId} as dispatched", id);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to mark outbox message {MessageId} as dispatched in {Schema}.{Table} on {Server}/{Database}",
                id,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task RescheduleAsync(Guid id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
    {
        var nextAttempt = timeProvider.GetUtcNow().Add(delay);

        logger.LogDebug(
            "Rescheduling outbox message {MessageId} for next attempt at {NextAttempt} due to error: {Error}",
            id, nextAttempt, lastError);

        var sql = $"""

                        UPDATE [{schemaName}].[{tableName}]
                        SET RetryCount = RetryCount + 1,
                            LastError = @LastError,
                            NextAttemptAt = @NextAttemptAt
                        WHERE Id = @Id
            """;

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                LastError = lastError,
                NextAttemptAt = nextAttempt,
            }).ConfigureAwait(false);

            logger.LogDebug("Successfully rescheduled outbox message {MessageId} for {NextAttempt}", id, nextAttempt);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to reschedule outbox message {MessageId} in {Schema}.{Table} on {Server}/{Database}",
                id,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task FailAsync(Guid id, string lastError, CancellationToken cancellationToken)
    {
        logger.LogWarning("Permanently failing outbox message {MessageId} due to error: {Error}", id, lastError);

        var sql = $"""

                        UPDATE [{schemaName}].[{tableName}]
                        SET IsProcessed = 1,
                            ProcessedAt = SYSDATETIMEOFFSET(),
                            ProcessedBy = @ProcessedBy,
                            LastError = @LastError
                        WHERE Id = @Id
            """;

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                ProcessedBy = $"{Environment.MachineName}:FAILED",
                LastError = lastError,
            }).ConfigureAwait(false);

            logger.LogWarning("Successfully marked outbox message {MessageId} as permanently failed", id);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to mark outbox message {MessageId} as failed in {Schema}.{Table} on {Server}/{Database}",
                id,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    private static (string Server, string Database) ParseConnectionInfo(string cs)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(cs);
            return (builder.DataSource ?? "unknown-server", builder.InitialCatalog ?? "unknown-database");
        }
        catch
        {
            return ("unknown-server", "unknown-database");
        }
    }
}
