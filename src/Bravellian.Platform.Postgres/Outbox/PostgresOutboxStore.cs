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

using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of IOutboxStore using SKIP LOCKED for concurrency.
/// </summary>
internal sealed class PostgresOutboxStore : IOutboxStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PostgresOutboxStore> logger;
    private readonly string serverName;
    private readonly string databaseName;
    private readonly OwnerToken ownerToken;
    private readonly int leaseSeconds;
    private readonly string qualifiedTableName;

    public PostgresOutboxStore(
        IOptions<PostgresOutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<PostgresOutboxStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;
        ownerToken = OwnerToken.GenerateNew();
        leaseSeconds = (int)opts.LeaseDuration.TotalSeconds;
        qualifiedTableName = PostgresSqlHelper.Qualify(schemaName, tableName);

        (serverName, databaseName) = ParseConnectionInfo(connectionString);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Claiming up to {Limit} outbox messages for processing with owner token {OwnerToken}",
            limit,
            ownerToken);

        var claimSql = $"""
            WITH cte AS (
                SELECT "Id"
                FROM {qualifiedTableName}
                WHERE "Status" = 0
                    AND ("LockedUntil" IS NULL OR "LockedUntil" <= CURRENT_TIMESTAMP)
                    AND ("DueTimeUtc" IS NULL OR "DueTimeUtc" <= CURRENT_TIMESTAMP)
                ORDER BY "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {qualifiedTableName} AS o
            SET "Status" = 1,
                "OwnerToken" = @OwnerToken,
                "LockedUntil" = CURRENT_TIMESTAMP + (@LeaseSeconds || ' seconds')::interval
            FROM cte
            WHERE o."Id" = cte."Id"
            RETURNING o."Id";
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var claimedIds = await connection.QueryAsync<Guid>(
                claimSql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = limit,
                }).ConfigureAwait(false);

            var idList = claimedIds.ToList();

            if (idList.Count == 0)
            {
                logger.LogDebug("No outbox messages claimed");
                return Array.Empty<OutboxMessage>();
            }

            var sql = $"""
                SELECT *
                FROM {qualifiedTableName}
                WHERE "Id" = ANY(@Ids);
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

    public async Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
    {
        logger.LogDebug("Marking outbox message {MessageId} as dispatched", id);

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 2,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "IsProcessed" = TRUE,
                "ProcessedAt" = CURRENT_TIMESTAMP
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = @Id;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(sql, new { OwnerToken = ownerToken.Value, Id = id.Value }).ConfigureAwait(false);

            await TryUpdateJoinCompletionAsync(connection, new[] { id.Value }).ConfigureAwait(false);

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

    public async Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be non-negative when rescheduling outbox messages.");
        }

        var now = timeProvider.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
        {
            logger.LogWarning("Time provider returned non-UTC timestamp {Timestamp}; reschedule times will be normalized to UTC.", now);
        }

        var nextAttempt = now.Add(delay);

        logger.LogDebug(
            "Rescheduling outbox message {MessageId} for next attempt at {NextAttempt} due to error: {Error}",
            id, nextAttempt, lastError);

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "RetryCount" = "RetryCount" + 1,
                "LastError" = COALESCE(@LastError, "LastError"),
                "DueTimeUtc" = COALESCE(@DueTimeUtc, "DueTimeUtc", CURRENT_TIMESTAMP)
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = @Id;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    Id = id.Value,
                    LastError = lastError,
                    DueTimeUtc = nextAttempt.UtcDateTime,
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

    public async Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
    {
        logger.LogWarning("Permanently failing outbox message {MessageId} due to error: {Error}", id, lastError);

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 3,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "IsProcessed" = FALSE,
                "LastError" = COALESCE(@LastError, "LastError"),
                "ProcessedBy" = COALESCE(@ProcessedBy, "ProcessedBy")
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = @Id;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    Id = id.Value,
                    LastError = lastError,
                    ProcessedBy = $"{Environment.MachineName}:FAILED",
                }).ConfigureAwait(false);

            await TryUpdateJoinFailureAsync(connection, new[] { id.Value }).ConfigureAwait(false);

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

    private async Task TryUpdateJoinCompletionAsync(NpgsqlConnection connection, Guid[] ids)
    {
        var joinMemberTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoinMember");
        var joinTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoin");

        var sql = $"""
            WITH updated AS (
                UPDATE {joinMemberTable}
                SET "CompletedAt" = CURRENT_TIMESTAMP
                WHERE "OutboxMessageId" = ANY(@Ids)
                    AND "CompletedAt" IS NULL
                    AND "FailedAt" IS NULL
                RETURNING "JoinId"
            ),
            counts AS (
                SELECT "JoinId", COUNT(*) AS "Count"
                FROM updated
                GROUP BY "JoinId"
            )
            UPDATE {joinTable} AS j
            SET "CompletedSteps" = j."CompletedSteps" + c."Count",
                "LastUpdatedUtc" = CURRENT_TIMESTAMP
            FROM counts c
            WHERE j."JoinId" = c."JoinId"
                AND (j."CompletedSteps" + j."FailedSteps") < j."ExpectedSteps";
            """;

        try
        {
            await connection.ExecuteAsync(sql, new { Ids = ids }).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P01", StringComparison.Ordinal))
        {
            // Join tables are optional; ignore missing-table errors.
        }
    }

    private async Task TryUpdateJoinFailureAsync(NpgsqlConnection connection, Guid[] ids)
    {
        var joinMemberTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoinMember");
        var joinTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoin");

        var sql = $"""
            WITH updated AS (
                UPDATE {joinMemberTable}
                SET "FailedAt" = CURRENT_TIMESTAMP
                WHERE "OutboxMessageId" = ANY(@Ids)
                    AND "CompletedAt" IS NULL
                    AND "FailedAt" IS NULL
                RETURNING "JoinId"
            ),
            counts AS (
                SELECT "JoinId", COUNT(*) AS "Count"
                FROM updated
                GROUP BY "JoinId"
            )
            UPDATE {joinTable} AS j
            SET "FailedSteps" = j."FailedSteps" + c."Count",
                "LastUpdatedUtc" = CURRENT_TIMESTAMP
            FROM counts c
            WHERE j."JoinId" = c."JoinId"
                AND (j."CompletedSteps" + j."FailedSteps") < j."ExpectedSteps";
            """;

        try
        {
            await connection.ExecuteAsync(sql, new { Ids = ids }).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P01", StringComparison.Ordinal))
        {
            // Join tables are optional; ignore missing-table errors.
        }
    }

    private static (string Server, string Database) ParseConnectionInfo(string cs)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(cs);
            return (builder.Host ?? "unknown-server", builder.Database ?? "unknown-database");
        }
        catch
        {
            return ("unknown-server", "unknown-database");
        }
    }
}
