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

using System.Collections.Generic;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of IInboxWorkStore using SKIP LOCKED for work queue semantics.
/// </summary>
internal sealed class PostgresInboxWorkStore : IInboxWorkStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PostgresInboxWorkStore> logger;
    private readonly string serverName;
    private readonly string databaseName;
    private readonly string qualifiedTableName;

    public PostgresInboxWorkStore(
        IOptions<PostgresInboxOptions> options,
        TimeProvider timeProvider,
        ILogger<PostgresInboxWorkStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;
        qualifiedTableName = PostgresSqlHelper.Qualify(schemaName, tableName);

        (serverName, databaseName) = ParseConnectionInfo(connectionString);
    }

    public async Task<IReadOnlyList<string>> ClaimAsync(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Claiming up to {BatchSize} inbox messages with {LeaseSeconds}s lease for owner {OwnerToken}",
            batchSize,
            leaseSeconds,
            ownerToken);

        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
            WITH cte AS (
                SELECT "MessageId"
                FROM {qualifiedTableName}
                WHERE "Status" IN ('Seen', 'Processing')
                    AND ("LockedUntil" IS NULL OR "LockedUntil" <= CURRENT_TIMESTAMP)
                    AND ("DueTimeUtc" IS NULL OR "DueTimeUtc" <= CURRENT_TIMESTAMP)
                ORDER BY "LastSeenUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {qualifiedTableName} AS i
            SET "Status" = 'Processing',
                "OwnerToken" = @OwnerToken,
                "LockedUntil" = CURRENT_TIMESTAMP + (@LeaseSeconds || ' seconds')::interval,
                "LastSeenUtc" = CURRENT_TIMESTAMP
            FROM cte
            WHERE i."MessageId" = cte."MessageId"
            RETURNING i."MessageId";
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var messageIds = await connection.QueryAsync<string>(
                sql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = batchSize,
                }).ConfigureAwait(false);

            var result = messageIds.ToList();
            logger.LogDebug(
                "Successfully claimed {ClaimedCount} inbox messages for owner {OwnerToken}",
                result.Count,
                ownerToken);

            SchedulerMetrics.InboxItemsClaimed.Add(
                result.Count,
                new KeyValuePair<string, object?>("queue", tableName),
                new KeyValuePair<string, object?>("store", schemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                result.Count,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));

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
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueClaimDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    public async Task AckAsync(
        OwnerToken ownerToken,
        IEnumerable<string> messageIds,
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToArray();
        if (messageIdList.Length == 0)
        {
            return;
        }

        logger.LogDebug(
            "Acknowledging {MessageCount} inbox messages for owner {OwnerToken}",
            messageIdList.Length,
            ownerToken);

        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 'Done',
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "ProcessedUtc" = CURRENT_TIMESTAMP,
                "LastSeenUtc" = CURRENT_TIMESTAMP
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 'Processing'
                AND "MessageId" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new { OwnerToken = ownerToken.Value, Ids = messageIdList }).ConfigureAwait(false);

            logger.LogDebug(
                "Successfully acknowledged {MessageCount} inbox messages for owner {OwnerToken}",
                messageIdList.Length,
                ownerToken);

            SchedulerMetrics.InboxItemsAcknowledged.Add(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", tableName),
                new KeyValuePair<string, object?>("store", schemaName));
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
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAckDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    public async Task AbandonAsync(
        OwnerToken ownerToken,
        IEnumerable<string> messageIds,
        string? lastError = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        var messageIdList = messageIds.ToArray();
        if (messageIdList.Length == 0)
        {
            return;
        }

        if (delay.HasValue && delay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be non-negative when abandoning inbox messages.");
        }

        logger.LogDebug(
            "Abandoning {MessageCount} inbox messages for owner {OwnerToken} with delay {DelayMs}ms",
            messageIdList.Length,
            ownerToken,
            delay?.TotalMilliseconds ?? 0);

        var stopwatch = Stopwatch.StartNew();
        var dueTimeUtc = delay.HasValue ? timeProvider.GetUtcNow().Add(delay.Value).UtcDateTime : (DateTime?)null;

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 'Seen',
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "LastSeenUtc" = CURRENT_TIMESTAMP,
                "Attempts" = "Attempts" + 1,
                "LastError" = COALESCE(@LastError, "LastError"),
                "DueTimeUtc" = COALESCE(@DueTimeUtc, "DueTimeUtc")
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 'Processing'
                AND "MessageId" = ANY(@Ids);
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
                    Ids = messageIdList,
                    LastError = lastError,
                    DueTimeUtc = dueTimeUtc,
                }).ConfigureAwait(false);

            logger.LogDebug(
                "Successfully abandoned {MessageCount} inbox messages for owner {OwnerToken}",
                messageIdList.Length,
                ownerToken);

            SchedulerMetrics.InboxItemsAbandoned.Add(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", tableName),
                new KeyValuePair<string, object?>("store", schemaName));
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
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAbandonDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    public async Task FailAsync(
        OwnerToken ownerToken,
        IEnumerable<string> messageIds,
        string error,
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToArray();
        if (messageIdList.Length == 0)
        {
            return;
        }

        logger.LogDebug(
            "Failing {MessageCount} inbox messages for owner {OwnerToken}: {Error}",
            messageIdList.Length,
            ownerToken,
            error);

        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 'Dead',
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "LastSeenUtc" = CURRENT_TIMESTAMP,
                "LastError" = COALESCE(@Reason, "LastError")
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 'Processing'
                AND "MessageId" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new { OwnerToken = ownerToken.Value, Ids = messageIdList, Reason = error }).ConfigureAwait(false);

            logger.LogWarning(
                "Failed {MessageCount} inbox messages for owner {OwnerToken}: {Error}",
                messageIdList.Length,
                ownerToken,
                error);

            SchedulerMetrics.InboxItemsFailed.Add(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", tableName),
                new KeyValuePair<string, object?>("store", schemaName));
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
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueFailDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    public async Task ReviveAsync(
        IEnumerable<string> messageIds,
        string? reason = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        var messageIdList = messageIds.ToArray();
        if (messageIdList.Length == 0)
        {
            return;
        }

        if (delay.HasValue && delay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be non-negative when reviving inbox messages.");
        }

        logger.LogInformation(
            "Reviving {MessageCount} dead inbox messages with delay {DelayMs}ms",
            messageIdList.Length,
            delay?.TotalMilliseconds ?? 0);

        var stopwatch = Stopwatch.StartNew();
        var dueTimeUtc = delay.HasValue ? timeProvider.GetUtcNow().Add(delay.Value).UtcDateTime : (DateTime?)null;

        var sql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 'Seen',
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "LastSeenUtc" = CURRENT_TIMESTAMP,
                "LastError" = @Reason,
                "DueTimeUtc" = @DueTimeUtc
            WHERE "Status" = 'Dead'
                AND "MessageId" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new
                {
                    Ids = messageIdList,
                    Reason = NormalizeReason(reason),
                    DueTimeUtc = dueTimeUtc,
                }).ConfigureAwait(false);

            SchedulerMetrics.InboxItemsRevived.Add(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", tableName),
                new KeyValuePair<string, object?>("store", schemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to revive inbox messages in {Schema}.{Table} on {Server}/{Database}",
                schemaName,
                tableName,
                serverName,
                databaseName);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueReviveDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                messageIdList.Length,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Reaping expired inbox leases");

        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
            WITH updated AS (
                UPDATE {qualifiedTableName}
                SET "Status" = 'Seen',
                    "OwnerToken" = NULL,
                    "LockedUntil" = NULL,
                    "LastSeenUtc" = CURRENT_TIMESTAMP
                WHERE "Status" = 'Processing'
                    AND "LockedUntil" IS NOT NULL
                    AND "LockedUntil" <= CURRENT_TIMESTAMP
                RETURNING 1
            )
            SELECT COUNT(*) FROM updated;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var result = await connection.ExecuteScalarAsync<int>(sql).ConfigureAwait(false);

            if (result > 0)
            {
                logger.LogInformation(
                    "Reaped {ReapedCount} expired inbox leases",
                    result);

                SchedulerMetrics.InboxItemsReaped.Add(
                    result,
                    new KeyValuePair<string, object?>("queue", tableName),
                    new KeyValuePair<string, object?>("store", schemaName));
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
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueReapDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "inbox"),
                new KeyValuePair<string, object?>("store", schemaName));
        }
    }

    public async Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        logger.LogDebug("Getting inbox message {MessageId}", messageId);

        var sql = $"""
            SELECT "MessageId", "Source", "Topic", "Payload", "Hash", "Attempts", "FirstSeenUtc", "LastSeenUtc", "DueTimeUtc", "LastError"
            FROM {qualifiedTableName}
            WHERE "MessageId" = @MessageId;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
