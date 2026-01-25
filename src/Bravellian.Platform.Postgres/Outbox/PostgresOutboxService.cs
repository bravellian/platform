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
using System.Data;
using System.Diagnostics;
using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

internal sealed class PostgresOutboxService : IOutbox
{
    private readonly PostgresOutboxOptions options;
    private readonly string connectionString;
    private readonly string qualifiedTableName;
    private readonly string enqueueSql;
    private readonly ILogger<PostgresOutboxService> logger;
    private readonly IOutboxJoinStore? joinStore;

    public PostgresOutboxService(
        IOptions<PostgresOutboxOptions> options,
        ILogger<PostgresOutboxService> logger,
        IOutboxJoinStore? joinStore = null)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.logger = logger;
        this.joinStore = joinStore;
        qualifiedTableName = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TableName);

        enqueueSql = $"""
            INSERT INTO {qualifiedTableName} ("Id", "Topic", "Payload", "CorrelationId", "MessageId", "DueTimeUtc")
            VALUES (@Id, @Topic, @Payload, @CorrelationId, @MessageId, @DueTimeUtc);
            """;
    }

    public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, (string?)null, null, cancellationToken);
    }

    public Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId, (DateTimeOffset?)null, cancellationToken);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken)
    {
        if (options.EnableSchemaDeployment)
        {
            await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                connectionString,
                options.SchemaName,
                options.TableName).ConfigureAwait(false);
        }

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.ExecuteAsync(
                enqueueSql,
                new
                {
                    Id = Guid.NewGuid(),
                    Topic = topic,
                    Payload = payload,
                    CorrelationId = correlationId,
                    MessageId = Guid.NewGuid(),
                    DueTimeUtc = dueTimeUtc?.UtcDateTime,
                },
                transaction: transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, transaction, null, null, cancellationToken);
    }

    public Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, transaction, correlationId, null, cancellationToken);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(transaction.Connection);

        await transaction.Connection.ExecuteAsync(
            enqueueSql,
            new
            {
                Id = Guid.NewGuid(),
                Topic = topic,
                Payload = payload,
                CorrelationId = correlationId,
                MessageId = Guid.NewGuid(),
                DueTimeUtc = dueTimeUtc?.UtcDateTime,
            },
            transaction: transaction).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.claim");
        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
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

            var claimed = await connection.QueryAsync<Guid>(
                sql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = batchSize,
                }).ConfigureAwait(false);

            var result = claimed.Select(OutboxWorkItemIdentifier.From).ToList();

            logger.LogDebug("Claimed {Count} outbox items with owner {OwnerToken}", result.Count, ownerToken);
            SchedulerMetrics.OutboxItemsClaimed.Add(
                result.Count,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                result.Count,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to claim outbox items with owner {OwnerToken}", ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueClaimDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task AckAsync(
        OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.ack");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.Select(id => id.Value).ToArray();

        if (idList.Length == 0)
        {
            return;
        }

        var updateSql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 2,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "IsProcessed" = TRUE,
                "ProcessedAt" = CURRENT_TIMESTAMP
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var affected = await connection.ExecuteAsync(
                updateSql,
                new { OwnerToken = ownerToken.Value, Ids = idList }).ConfigureAwait(false);

            if (affected > 0 && joinStore != null)
            {
                await ApplyJoinCompletionAsync(connection, idList).ConfigureAwait(false);
            }

            logger.LogDebug("Acknowledged {Count} outbox items with owner {OwnerToken}", idList.Length, ownerToken);
            SchedulerMetrics.OutboxItemsAcknowledged.Add(
                idList.Length,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acknowledge {Count} outbox items with owner {OwnerToken}", idList.Length, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAckDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Length,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task AbandonAsync(
        OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.abandon");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.Select(id => id.Value).ToArray();

        if (idList.Length == 0)
        {
            return;
        }

        var updateSql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "RetryCount" = "RetryCount" + 1,
                "LastError" = COALESCE(@LastError, "LastError"),
                "DueTimeUtc" = COALESCE(@DueTimeUtc, "DueTimeUtc", CURRENT_TIMESTAMP)
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                updateSql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    Ids = idList,
                    LastError = (string?)null,
                    DueTimeUtc = (DateTime?)null,
                }).ConfigureAwait(false);

            logger.LogDebug("Abandoned {Count} outbox items with owner {OwnerToken}", idList.Length, ownerToken);
            SchedulerMetrics.OutboxItemsAbandoned.Add(
                idList.Length,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to abandon {Count} outbox items with owner {OwnerToken}", idList.Length, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAbandonDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Length,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task FailAsync(
        OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.fail");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.Select(id => id.Value).ToArray();

        if (idList.Length == 0)
        {
            return;
        }

        var updateSql = $"""
            UPDATE {qualifiedTableName}
            SET "Status" = 3,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "IsProcessed" = FALSE,
                "LastError" = COALESCE(@LastError, "LastError"),
                "ProcessedBy" = COALESCE(@ProcessedBy, "ProcessedBy")
            WHERE "OwnerToken" = @OwnerToken
                AND "Status" = 1
                AND "Id" = ANY(@Ids);
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var affected = await connection.ExecuteAsync(
                updateSql,
                new
                {
                    OwnerToken = ownerToken.Value,
                    Ids = idList,
                    LastError = (string?)null,
                    ProcessedBy = $"{Environment.MachineName}:FAILED",
                }).ConfigureAwait(false);

            if (affected > 0 && joinStore != null)
            {
                await ApplyJoinFailureAsync(connection, idList).ConfigureAwait(false);
            }

            logger.LogDebug("Failed {Count} outbox items with owner {OwnerToken}", idList.Length, ownerToken);
            SchedulerMetrics.OutboxItemsFailed.Add(
                idList.Length,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark {Count} outbox items as failed with owner {OwnerToken}", idList.Length, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueFailDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Length,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.reap_expired");
        var stopwatch = Stopwatch.StartNew();

        var sql = $"""
            WITH updated AS (
                UPDATE {qualifiedTableName}
                SET "Status" = 0,
                    "OwnerToken" = NULL,
                    "LockedUntil" = NULL
                WHERE "Status" = 1
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

            var count = await connection.ExecuteScalarAsync<int>(sql).ConfigureAwait(false);

            logger.LogDebug("Reaped {Count} expired outbox items", count);
            SchedulerMetrics.OutboxItemsReaped.Add(
                count,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reap expired outbox items");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueReapDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task<JoinIdentifier> StartJoinAsync(
        long tenantId,
        int expectedSteps,
        string? metadata,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        if (options.EnableSchemaDeployment)
        {
            await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(
                connectionString,
                options.SchemaName).ConfigureAwait(false);
        }

        var join = await joinStore.CreateJoinAsync(
            tenantId,
            expectedSteps,
            metadata,
            cancellationToken).ConfigureAwait(false);

        return join.JoinId;
    }

    public async Task AttachMessageToJoinAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.AttachMessageToJoinAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportStepCompletedAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.IncrementCompletedAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportStepFailedAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.IncrementFailedAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyJoinCompletionAsync(NpgsqlConnection connection, Guid[] ids)
    {
        var joinMemberTable = PostgresSqlHelper.Qualify(options.SchemaName, "OutboxJoinMember");
        var joinTable = PostgresSqlHelper.Qualify(options.SchemaName, "OutboxJoin");

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

        await connection.ExecuteAsync(sql, new { Ids = ids }).ConfigureAwait(false);
    }

    private async Task ApplyJoinFailureAsync(NpgsqlConnection connection, Guid[] ids)
    {
        var joinMemberTable = PostgresSqlHelper.Qualify(options.SchemaName, "OutboxJoinMember");
        var joinTable = PostgresSqlHelper.Qualify(options.SchemaName, "OutboxJoin");

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

        await connection.ExecuteAsync(sql, new { Ids = ids }).ConfigureAwait(false);
    }
}
