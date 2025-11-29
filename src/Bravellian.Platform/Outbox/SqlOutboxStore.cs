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
    private readonly Guid ownerToken;
    private readonly int leaseSeconds;

    public SqlOutboxStore(IOptions<SqlOutboxOptions> options, TimeProvider timeProvider, ILogger<SqlOutboxStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;
        ownerToken = Guid.NewGuid();
        leaseSeconds = (int)opts.LeaseDuration.TotalSeconds;

        (serverName, databaseName) = ParseConnectionInfo(connectionString);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Claiming up to {Limit} outbox messages for processing with owner token {OwnerToken}",
            limit,
            ownerToken);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Call the work queue Claim stored procedure
            var claimedIds = await connection.QueryAsync<Guid>(
                $"[{schemaName}].[{tableName}_Claim]",
                new
                {
                    OwnerToken = ownerToken,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = limit,
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            var idList = claimedIds.ToList();
            
            if (idList.Count == 0)
            {
                logger.LogDebug("No outbox messages claimed");
                return Array.Empty<OutboxMessage>();
            }

            // Fetch the full message details for claimed IDs
            var sql = $"""
                SELECT * 
                FROM [{schemaName}].[{tableName}]
                WHERE Id IN @Ids
                """;

            var messages = await connection.QueryAsync<OutboxMessage>(sql, new { Ids = idList }).ConfigureAwait(false);
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

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Create a table-valued parameter with the single ID
            var idsTable = CreateGuidIdTable(new[] { id });
            using var command = new SqlCommand($"[{schemaName}].[{tableName}_Ack]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[GuidIdList]";

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use Abandon to release the lock and update retry count, error, and due time atomically
            var idsTable = CreateGuidIdTable(new[] { id });
            using var abandonCommand = new SqlCommand($"[{schemaName}].[{tableName}_Abandon]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            abandonCommand.Parameters.AddWithValue("@OwnerToken", ownerToken);
            abandonCommand.Parameters.AddWithValue("@LastError", lastError ?? (object)DBNull.Value);
            abandonCommand.Parameters.AddWithValue("@DueTimeUtc", nextAttempt.UtcDateTime);
            var parameter = abandonCommand.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[GuidIdList]";

            await abandonCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use Fail stored procedure to mark as permanently failed with error and machine info
            var idsTable = CreateGuidIdTable(new[] { id });
            using var command = new SqlCommand($"[{schemaName}].[{tableName}_Fail]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            command.Parameters.AddWithValue("@LastError", lastError ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ProcessedBy", $"{Environment.MachineName}:FAILED");
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[GuidIdList]";

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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

    private static System.Data.DataTable CreateGuidIdTable(IEnumerable<Guid> ids)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("Id", typeof(Guid));

        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
    }
}
