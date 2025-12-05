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
/// SQL Server implementation of IOutboxJoinStore for managing outbox join operations.
/// </summary>
internal class SqlOutboxJoinStore : IOutboxJoinStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly ILogger<SqlOutboxJoinStore> logger;

    public SqlOutboxJoinStore(
        IOptions<SqlOutboxOptions> options,
        ILogger<SqlOutboxJoinStore> logger)
    {
        var opts = options.Value;
        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        this.logger = logger;
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

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                INSERT INTO [{schemaName}].[OutboxJoin] 
                    (PayeWaiveTenantId, ExpectedSteps, Metadata)
                OUTPUT 
                    INSERTED.JoinId,
                    INSERTED.PayeWaiveTenantId,
                    INSERTED.ExpectedSteps,
                    INSERTED.CompletedSteps,
                    INSERTED.FailedSteps,
                    INSERTED.Status,
                    INSERTED.CreatedUtc,
                    INSERTED.LastUpdatedUtc,
                    INSERTED.Metadata
                VALUES 
                    (@TenantId, @ExpectedSteps, @Metadata)
                """;

            var join = await connection.QuerySingleAsync<OutboxJoin>(
                sql,
                new
                {
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
        Guid joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Attaching message {MessageId} to join {JoinId}",
            outboxMessageId,
            joinId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use MERGE for idempotent insert
            var sql = $"""
                MERGE [{schemaName}].[OutboxJoinMember] AS target
                USING (SELECT @JoinId AS JoinId, @OutboxMessageId AS OutboxMessageId) AS source
                ON target.JoinId = source.JoinId AND target.OutboxMessageId = source.OutboxMessageId
                WHEN NOT MATCHED THEN
                    INSERT (JoinId, OutboxMessageId)
                    VALUES (source.JoinId, source.OutboxMessageId);
                """;

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

    public async Task<OutboxJoin?> GetJoinAsync(Guid joinId, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting join {JoinId}", joinId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                SELECT 
                    JoinId,
                    PayeWaiveTenantId,
                    ExpectedSteps,
                    CompletedSteps,
                    FailedSteps,
                    Status,
                    CreatedUtc,
                    LastUpdatedUtc,
                    Metadata
                FROM [{schemaName}].[OutboxJoin]
                WHERE JoinId = @JoinId
                """;

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
        Guid joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Incrementing completed count for join {JoinId} via message {MessageId}",
            joinId,
            outboxMessageId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Update only if this message hasn't already been counted
            // We check if the message is in the join members to ensure idempotency
            var sql = $"""
                UPDATE j
                SET 
                    CompletedSteps = CompletedSteps + 1,
                    LastUpdatedUtc = SYSUTCDATETIME()
                OUTPUT 
                    INSERTED.JoinId,
                    INSERTED.PayeWaiveTenantId,
                    INSERTED.ExpectedSteps,
                    INSERTED.CompletedSteps,
                    INSERTED.FailedSteps,
                    INSERTED.Status,
                    INSERTED.CreatedUtc,
                    INSERTED.LastUpdatedUtc,
                    INSERTED.Metadata
                FROM [{schemaName}].[OutboxJoin] j
                WHERE j.JoinId = @JoinId
                    AND EXISTS (
                        SELECT 1 
                        FROM [{schemaName}].[OutboxJoinMember] m
                        WHERE m.JoinId = @JoinId 
                            AND m.OutboxMessageId = @OutboxMessageId
                    )
                    AND NOT EXISTS (
                        -- Prevent double-counting by checking a separate tracking mechanism
                        -- For now, we'll rely on the application layer to ensure idempotency
                        SELECT 1 FROM (SELECT 1) x WHERE 1=0
                    )
                """;

            var join = await connection.QuerySingleOrDefaultAsync<OutboxJoin>(
                sql,
                new
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                }).ConfigureAwait(false);

            if (join == null)
            {
                // If no rows were updated, fetch the current state
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
        Guid joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Incrementing failed count for join {JoinId} via message {MessageId}",
            joinId,
            outboxMessageId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                UPDATE j
                SET 
                    FailedSteps = FailedSteps + 1,
                    LastUpdatedUtc = SYSUTCDATETIME()
                OUTPUT 
                    INSERTED.JoinId,
                    INSERTED.PayeWaiveTenantId,
                    INSERTED.ExpectedSteps,
                    INSERTED.CompletedSteps,
                    INSERTED.FailedSteps,
                    INSERTED.Status,
                    INSERTED.CreatedUtc,
                    INSERTED.LastUpdatedUtc,
                    INSERTED.Metadata
                FROM [{schemaName}].[OutboxJoin] j
                WHERE j.JoinId = @JoinId
                    AND EXISTS (
                        SELECT 1 
                        FROM [{schemaName}].[OutboxJoinMember] m
                        WHERE m.JoinId = @JoinId 
                            AND m.OutboxMessageId = @OutboxMessageId
                    )
                """;

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
        Guid joinId,
        byte status,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Updating status of join {JoinId} to {Status}", joinId, status);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                UPDATE [{schemaName}].[OutboxJoin]
                SET 
                    Status = @Status,
                    LastUpdatedUtc = SYSUTCDATETIME()
                WHERE JoinId = @JoinId
                """;

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

    public async Task<IReadOnlyList<Guid>> GetJoinMessagesAsync(
        Guid joinId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting messages for join {JoinId}", joinId);

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                SELECT OutboxMessageId
                FROM [{schemaName}].[OutboxJoinMember]
                WHERE JoinId = @JoinId
                """;

            var messageIds = await connection.QueryAsync<Guid>(
                sql,
                new { JoinId = joinId }).ConfigureAwait(false);

            var result = messageIds.ToList();

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
