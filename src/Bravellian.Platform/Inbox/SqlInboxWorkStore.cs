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
/// SQL Server implementation of IInboxWorkStore using work queue stored procedures.
/// </summary>
internal class SqlInboxWorkStore : IInboxWorkStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlInboxWorkStore> logger;
    private readonly string serverName;
    private readonly string databaseName;

    public SqlInboxWorkStore(IOptions<SqlInboxOptions> options, TimeProvider timeProvider, ILogger<SqlInboxWorkStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;
        (serverName, databaseName) = ParseConnectionInfo(connectionString);
    }

    public async Task<IReadOnlyList<string>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Claiming up to {BatchSize} inbox messages with {LeaseSeconds}s lease for owner {OwnerToken}",
            batchSize,
            leaseSeconds,
            ownerToken);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var messageIds = await connection.QueryAsync<string>(
                $"[{schemaName}].[{tableName}_Claim]",
                new
                {
                    OwnerToken = ownerToken,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = batchSize,
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            var result = messageIds.ToList();
            logger.LogDebug(
                "Successfully claimed {ClaimedCount} inbox messages for owner {OwnerToken}",
                result.Count,
                ownerToken);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to claim inbox messages for owner {OwnerToken} in {Schema}.{Table} on {Server}/{Database}",
                ownerToken,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task AckAsync(
        Guid ownerToken,
        IEnumerable<string> messageIds,
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        logger.LogDebug(
            "Acknowledging {MessageCount} inbox messages for owner {OwnerToken}",
            messageIdList.Count,
            ownerToken);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            using var command = new SqlCommand($"[{schemaName}].[{tableName}_Ack]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[StringIdList]";

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Successfully acknowledged {MessageCount} inbox messages for owner {OwnerToken}",
                messageIdList.Count,
                ownerToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to acknowledge inbox messages for owner {OwnerToken} in {Schema}.{Table} on {Server}/{Database}",
                ownerToken,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<string> messageIds,
        string? lastError = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        logger.LogDebug(
            "Abandoning {MessageCount} inbox messages for owner {OwnerToken} with delay {DelayMs}ms",
            messageIdList.Count,
            ownerToken,
            delay?.TotalMilliseconds ?? 0);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            using var command = new SqlCommand($"[{schemaName}].[{tableName}_Abandon]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[StringIdList]";
            command.Parameters.AddWithValue("@LastError", lastError ?? (object)DBNull.Value);
            
            // Calculate due time if delay is specified
            if (delay.HasValue)
            {
                var dueTime = timeProvider.GetUtcNow().Add(delay.Value);
                command.Parameters.AddWithValue("@DueTimeUtc", dueTime.UtcDateTime);
            }
            else
            {
                command.Parameters.AddWithValue("@DueTimeUtc", DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Successfully abandoned {MessageCount} inbox messages for owner {OwnerToken}",
                messageIdList.Count,
                ownerToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to abandon inbox messages for owner {OwnerToken} in {Schema}.{Table} on {Server}/{Database}",
                ownerToken,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task FailAsync(
        Guid ownerToken,
        IEnumerable<string> messageIds,
        string error,
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        logger.LogDebug(
            "Failing {MessageCount} inbox messages for owner {OwnerToken}: {Error}",
            messageIdList.Count,
            ownerToken,
            error);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            using var command = new SqlCommand($"[{schemaName}].[{tableName}_Fail]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = $"[{schemaName}].[StringIdList]";
            command.Parameters.AddWithValue("@Reason", error ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Failed {MessageCount} inbox messages for owner {OwnerToken}: {Error}",
                messageIdList.Count,
                ownerToken,
                error);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to mark inbox messages as failed for owner {OwnerToken} in {Schema}.{Table} on {Server}/{Database}",
                ownerToken,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Reaping expired inbox leases");

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var result = await connection.QuerySingleAsync<int>(
                $"[{schemaName}].[{tableName}_ReapExpired]",
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            if (result > 0)
            {
                logger.LogInformation(
                    "Reaped {ReapedCount} expired inbox leases",
                    result);
            }
            else
            {
                logger.LogDebug("No expired inbox leases found to reap");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to reap expired inbox leases in {Schema}.{Table} on {Server}/{Database}",
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    public async Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        logger.LogDebug("Getting inbox message {MessageId}", messageId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""

                                SELECT MessageId, Source, Topic, Payload, Hash, Attempts, FirstSeenUtc, LastSeenUtc, DueTimeUtc, LastError
                                FROM [{schemaName}].[{tableName}]
                                WHERE MessageId = @MessageId
                """;

            var row = await connection.QuerySingleOrDefaultAsync(sql, new { MessageId = messageId }).ConfigureAwait(false);

            if (row == null)
            {
                throw new InvalidOperationException($"Inbox message '{messageId}' not found");
            }

            return new InboxMessage
            {
                MessageId = row.MessageId,
                Source = row.Source ?? string.Empty,
                Topic = row.Topic ?? string.Empty,
                Payload = row.Payload ?? string.Empty,
                Hash = row.Hash,
                Attempt = row.Attempts,
                FirstSeenUtc = row.FirstSeenUtc,
                LastSeenUtc = row.LastSeenUtc,
                DueTimeUtc = row.DueTimeUtc,
                LastError = row.LastError,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to get inbox message {MessageId} from {Schema}.{Table} on {Server}/{Database}",
                messageId,
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
    }

    private static System.Data.DataTable CreateStringIdTable(IList<string> ids)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("Id", typeof(string));

        // Pre-size the table to avoid dynamic resizing
        table.MinimumCapacity = ids.Count;

        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
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
