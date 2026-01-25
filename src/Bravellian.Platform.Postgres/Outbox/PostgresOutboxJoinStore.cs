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
/// PostgreSQL implementation of IOutboxJoinStore for managing outbox join operations.
/// </summary>
internal sealed class PostgresOutboxJoinStore : IOutboxJoinStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly ILogger<PostgresOutboxJoinStore> logger;
    private readonly string joinTable;
    private readonly string joinMemberTable;

    public PostgresOutboxJoinStore(
        IOptions<PostgresOutboxOptions> options,
        ILogger<PostgresOutboxJoinStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        this.logger = logger;
        joinTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoin");
        joinMemberTable = PostgresSqlHelper.Qualify(schemaName, "OutboxJoinMember");
    }

    public async Task<OutboxJoin> CreateJoinAsync(
        long tenantId,
        int expectedSteps,
        string? metadata,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Creating join for tenant {TenantId} with {ExpectedSteps} expected steps",
            tenantId,
            expectedSteps);

        var sql = $"""
            INSERT INTO {joinTable}
                ("JoinId", "PayeWaiveTenantId", "ExpectedSteps", "Metadata")
            VALUES
                (@JoinId, @TenantId, @ExpectedSteps, @Metadata)
            RETURNING
                "JoinId",
                "PayeWaiveTenantId" AS "TenantId",
                "ExpectedSteps",
                "CompletedSteps",
                "FailedSteps",
                "Status",
                "CreatedUtc",
                "LastUpdatedUtc",
                "Metadata";
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var join = await connection.QuerySingleAsync<OutboxJoin>(
                sql,
                new
                {
                    JoinId = Guid.NewGuid(),
                    TenantId = tenantId,
                    ExpectedSteps = expectedSteps,
                    Metadata = metadata,
                }).ConfigureAwait(false);

            logger.LogDebug(
                "Created join {JoinId} for tenant {TenantId}",
                join.JoinId,
                tenantId);

            return join;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create join for tenant {TenantId}",
                tenantId);
            throw;
        }
    }

    public async Task AttachMessageToJoinAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Attaching message {MessageId} to join {JoinId}",
            outboxMessageId,
            joinId);

        var sql = $"""
            INSERT INTO {joinMemberTable} ("JoinId", "OutboxMessageId")
            VALUES (@JoinId, @OutboxMessageId)
            ON CONFLICT ("JoinId", "OutboxMessageId") DO NOTHING;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                sql,
                new
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                }).ConfigureAwait(false);

            logger.LogDebug(
                "Attached message {MessageId} to join {JoinId}",
                outboxMessageId,
                joinId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to attach message {MessageId} to join {JoinId}",
                outboxMessageId,
                joinId);
            throw;
        }
    }

    public async Task<OutboxJoin?> GetJoinAsync(JoinIdentifier joinId, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting join {JoinId}", joinId);

        var sql = $"""
            SELECT
                "JoinId",
                "PayeWaiveTenantId" AS "TenantId",
                "ExpectedSteps",
                "CompletedSteps",
                "FailedSteps",
                "Status",
                "CreatedUtc",
                "LastUpdatedUtc",
                "Metadata"
            FROM {joinTable}
            WHERE "JoinId" = @JoinId;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var join = await connection.QuerySingleOrDefaultAsync<OutboxJoin>(
                sql,
                new { JoinId = joinId }).ConfigureAwait(false);

            if (join == null)
            {
                logger.LogDebug("Join {JoinId} not found", joinId);
            }

            return join;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get join {JoinId}", joinId);
            throw;
        }
    }

    public async Task<OutboxJoin> IncrementCompletedAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Incrementing completed count for join {JoinId} via message {MessageId}",
            joinId,
            outboxMessageId);

        var sql = $"""
            WITH updated_member AS (
                UPDATE {joinMemberTable}
                SET "CompletedAt" = CURRENT_TIMESTAMP
                WHERE "JoinId" = @JoinId
                    AND "OutboxMessageId" = @OutboxMessageId
                    AND "CompletedAt" IS NULL
                    AND "FailedAt" IS NULL
                RETURNING "JoinId"
            ),
            updated_join AS (
                UPDATE {joinTable} AS j
                SET "CompletedSteps" = j."CompletedSteps" + 1,
                    "LastUpdatedUtc" = CURRENT_TIMESTAMP
                FROM updated_member m
                WHERE j."JoinId" = m."JoinId"
                    AND (j."CompletedSteps" + j."FailedSteps") < j."ExpectedSteps"
                RETURNING
                    j."JoinId",
                    j."PayeWaiveTenantId" AS "TenantId",
                    j."ExpectedSteps",
                    j."CompletedSteps",
                    j."FailedSteps",
                    j."Status",
                    j."CreatedUtc",
                    j."LastUpdatedUtc",
                    j."Metadata"
            )
            SELECT * FROM updated_join;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var join = await connection.QuerySingleOrDefaultAsync<OutboxJoin>(
                sql,
                new
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                }).ConfigureAwait(false);

            if (join == null)
            {
                logger.LogDebug(
                    "No update performed for join {JoinId} - message {MessageId} may have already been counted or join doesn't exist",
                    joinId,
                    outboxMessageId);

                var currentJoin = await GetJoinAsync(joinId, cancellationToken).ConfigureAwait(false);
                if (currentJoin == null)
                {
                    throw new InvalidOperationException($"Join {joinId} not found");
                }

                return currentJoin;
            }

            logger.LogDebug(
                "Incremented completed count for join {JoinId} to {CompletedSteps}",
                joinId,
                join.CompletedSteps);

            return join;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to increment completed count for join {JoinId}",
                joinId);
            throw;
        }
    }

    public async Task<OutboxJoin> IncrementFailedAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Incrementing failed count for join {JoinId} via message {MessageId}",
            joinId,
            outboxMessageId);

        var sql = $"""
            WITH updated_member AS (
                UPDATE {joinMemberTable}
                SET "FailedAt" = CURRENT_TIMESTAMP
                WHERE "JoinId" = @JoinId
                    AND "OutboxMessageId" = @OutboxMessageId
                    AND "CompletedAt" IS NULL
                    AND "FailedAt" IS NULL
                RETURNING "JoinId"
            ),
            updated_join AS (
                UPDATE {joinTable} AS j
                SET "FailedSteps" = j."FailedSteps" + 1,
                    "LastUpdatedUtc" = CURRENT_TIMESTAMP
                FROM updated_member m
                WHERE j."JoinId" = m."JoinId"
                    AND (j."CompletedSteps" + j."FailedSteps") < j."ExpectedSteps"
                RETURNING
                    j."JoinId",
                    j."PayeWaiveTenantId" AS "TenantId",
                    j."ExpectedSteps",
                    j."CompletedSteps",
                    j."FailedSteps",
                    j."Status",
                    j."CreatedUtc",
                    j."LastUpdatedUtc",
                    j."Metadata"
            )
            SELECT * FROM updated_join;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var join = await connection.QuerySingleOrDefaultAsync<OutboxJoin>(
                sql,
                new
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                }).ConfigureAwait(false);

            if (join == null)
            {
                logger.LogDebug(
                    "No update performed for join {JoinId} - fetching current state",
                    joinId);

                var currentJoin = await GetJoinAsync(joinId, cancellationToken).ConfigureAwait(false);
                if (currentJoin == null)
                {
                    throw new InvalidOperationException($"Join {joinId} not found");
                }

                return currentJoin;
            }

            logger.LogDebug(
                "Incremented failed count for join {JoinId} to {FailedSteps}",
                joinId,
                join.FailedSteps);

            return join;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to increment failed count for join {JoinId}",
                joinId);
            throw;
        }
    }

    public async Task UpdateStatusAsync(
        JoinIdentifier joinId,
        byte status,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Updating status of join {JoinId} to {Status}", joinId, status);

        var sql = $"""
            UPDATE {joinTable}
            SET "Status" = @Status,
                "LastUpdatedUtc" = CURRENT_TIMESTAMP
            WHERE "JoinId" = @JoinId;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                sql,
                new
                {
                    JoinId = joinId,
                    Status = status,
                }).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                logger.LogWarning("Join {JoinId} not found for status update", joinId);
            }
            else
            {
                logger.LogDebug("Updated status of join {JoinId} to {Status}", joinId, status);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update status of join {JoinId}",
                joinId);
            throw;
        }
    }

    public async Task<IReadOnlyList<OutboxMessageIdentifier>> GetJoinMessagesAsync(
        JoinIdentifier joinId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting messages for join {JoinId}", joinId);

        var sql = $"""
            SELECT "OutboxMessageId"
            FROM {joinMemberTable}
            WHERE "JoinId" = @JoinId;
            """;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var messageIds = await connection.QueryAsync<Guid>(
                sql,
                new { JoinId = joinId }).ConfigureAwait(false);

            var result = messageIds.Select(OutboxMessageIdentifier.From).ToList();

            logger.LogDebug(
                "Found {Count} messages for join {JoinId}",
                result.Count,
                joinId);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to get messages for join {JoinId}",
                joinId);
            throw;
        }
    }
}
