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

using System.Diagnostics.CodeAnalysis;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal sealed class SqlExternalSideEffectStore : IExternalSideEffectStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlExternalSideEffectStore> logger;
    private readonly OwnerToken ownerToken;

    public SqlExternalSideEffectStore(
        IOptions<SqlExternalSideEffectOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlExternalSideEffectStore> logger)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            throw new ArgumentException("ConnectionString must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(opts.SchemaName))
        {
            throw new ArgumentException("SchemaName must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(opts.TableName))
        {
            throw new ArgumentException("TableName must be provided.", nameof(options));
        }

        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        this.timeProvider = timeProvider;
        this.logger = logger;
        ownerToken = OwnerToken.GenerateNew();
    }

    public async Task<ExternalSideEffectRecord?> GetAsync(ExternalSideEffectKey key, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            SELECT TOP(1) *
            FROM [{schemaName}].[{tableName}]
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey
            """;

        return await connection.QuerySingleOrDefaultAsync<ExternalSideEffectRecord>(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
            }).ConfigureAwait(false);
    }

    public async Task<ExternalSideEffectRecord> GetOrCreateAsync(ExternalSideEffectRequest request, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var insertSql = $"""
            INSERT INTO [{schemaName}].[{tableName}] (
                OperationName,
                IdempotencyKey,
                Status,
                CorrelationId,
                OutboxMessageId,
                PayloadHash)
            VALUES (
                @OperationName,
                @IdempotencyKey,
                @Status,
                @CorrelationId,
                @OutboxMessageId,
                @PayloadHash);
            """;

        try
        {
            await connection.ExecuteAsync(
                insertSql,
                new
                {
                    OperationName = request.Key.OperationName,
                    IdempotencyKey = request.Key.IdempotencyKey,
                    Status = ExternalSideEffectStatus.Pending,
                    request.CorrelationId,
                    request.OutboxMessageId,
                    request.PayloadHash,
                }).ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            logger.LogDebug(
                "External side-effect record already exists for {OperationName}/{IdempotencyKey}",
                request.Key.OperationName,
                request.Key.IdempotencyKey);
        }

        var record = await GetAsync(request.Key, cancellationToken).ConfigureAwait(false);
        if (record == null)
        {
            throw new InvalidOperationException("External side-effect record could not be created or retrieved.");
        }

        return record;
    }

    [SuppressMessage("Reliability", "CA1849:Call async methods when available", Justification = "SqlTransaction is required by helper methods.")]
    public async Task<ExternalSideEffectAttempt> TryBeginAttemptAsync(
        ExternalSideEffectKey key,
        TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration), lockDuration, "Lock duration must be positive.");
        }

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var record = await GetRecordForUpdateAsync(connection, transaction, key).ConfigureAwait(false);
        if (record == null)
        {
            await InsertRecordAsync(connection, transaction, key).ConfigureAwait(false);
            record = await GetRecordForUpdateAsync(connection, transaction, key).ConfigureAwait(false);
        }

        if (record == null)
        {
            transaction.Commit();
            throw new InvalidOperationException("External side-effect record could not be created or retrieved.");
        }

        if (record.Status == ExternalSideEffectStatus.Succeeded || record.Status == ExternalSideEffectStatus.Failed)
        {
            transaction.Commit();
            return new ExternalSideEffectAttempt(ExternalSideEffectAttemptDecision.AlreadyCompleted, record);
        }

        var now = timeProvider.GetUtcNow();
        if (record.LockedUntil is DateTimeOffset lockedUntil && lockedUntil > now && record.LockedBy != ownerToken.Value)
        {
            transaction.Commit();
            return new ExternalSideEffectAttempt(
                ExternalSideEffectAttemptDecision.Locked,
                record,
                $"Locked until {lockedUntil:O}.");
        }

        var updated = await MarkInFlightAsync(connection, transaction, key, now, lockDuration).ConfigureAwait(false);
        transaction.Commit();
        return new ExternalSideEffectAttempt(ExternalSideEffectAttemptDecision.Ready, updated);
    }

    public async Task RecordExternalCheckAsync(
        ExternalSideEffectKey key,
        ExternalSideEffectCheckResult result,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            UPDATE [{schemaName}].[{tableName}]
            SET LastExternalCheckAt = @CheckedAt,
                ExternalReferenceId = COALESCE(@ExternalReferenceId, ExternalReferenceId),
                ExternalStatus = COALESCE(@ExternalStatus, ExternalStatus),
                LastUpdatedAt = @CheckedAt
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey;
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
                CheckedAt = checkedAt,
                result.ExternalReferenceId,
                result.ExternalStatus,
            }).ConfigureAwait(false);
    }

    public async Task MarkSucceededAsync(
        ExternalSideEffectKey key,
        ExternalSideEffectExecutionResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                ExternalReferenceId = @ExternalReferenceId,
                ExternalStatus = @ExternalStatus,
                LastError = NULL,
                LockedUntil = NULL,
                LockedBy = NULL,
                LastUpdatedAt = @CompletedAt
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey;
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
                Status = ExternalSideEffectStatus.Succeeded,
                result.ExternalReferenceId,
                result.ExternalStatus,
                CompletedAt = completedAt,
            }).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        ExternalSideEffectKey key,
        string errorMessage,
        bool isPermanent,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                LastError = @LastError,
                LockedUntil = NULL,
                LockedBy = NULL,
                LastUpdatedAt = @FailedAt
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey;
            """;

        var status = isPermanent ? ExternalSideEffectStatus.Failed : ExternalSideEffectStatus.Pending;

        await connection.ExecuteAsync(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
                Status = status,
                LastError = errorMessage,
                FailedAt = failedAt,
            }).ConfigureAwait(false);
    }

    private async Task<ExternalSideEffectRecord?> GetRecordForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExternalSideEffectKey key)
    {
        var sql = $"""
            SELECT TOP(1) *
            FROM [{schemaName}].[{tableName}] WITH (UPDLOCK, ROWLOCK)
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey;
            """;

        return await connection.QuerySingleOrDefaultAsync<ExternalSideEffectRecord>(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
            },
            transaction).ConfigureAwait(false);
    }

    private async Task InsertRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExternalSideEffectKey key)
    {
        var insertSql = $"""
            INSERT INTO [{schemaName}].[{tableName}] (OperationName, IdempotencyKey, Status)
            VALUES (@OperationName, @IdempotencyKey, @Status);
            """;

        try
        {
            await connection.ExecuteAsync(
                insertSql,
                new
                {
                    OperationName = key.OperationName,
                    IdempotencyKey = key.IdempotencyKey,
                    Status = ExternalSideEffectStatus.Pending,
                },
                transaction).ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            logger.LogDebug(
                "External side-effect record already exists for {OperationName}/{IdempotencyKey}",
                key.OperationName,
                key.IdempotencyKey);
        }
    }

    private async Task<ExternalSideEffectRecord> MarkInFlightAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExternalSideEffectKey key,
        DateTimeOffset now,
        TimeSpan lockDuration)
    {
        var lockedUntil = now.Add(lockDuration);
        var sql = $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                AttemptCount = AttemptCount + 1,
                LastAttemptAt = @AttemptAt,
                LastUpdatedAt = @AttemptAt,
                LockedUntil = @LockedUntil,
                LockedBy = @LockedBy,
                LastError = NULL
            OUTPUT inserted.*
            WHERE OperationName = @OperationName
              AND IdempotencyKey = @IdempotencyKey;
            """;

        return await connection.QuerySingleAsync<ExternalSideEffectRecord>(
            sql,
            new
            {
                OperationName = key.OperationName,
                IdempotencyKey = key.IdempotencyKey,
                Status = ExternalSideEffectStatus.InFlight,
                AttemptAt = now,
                LockedUntil = lockedUntil,
                LockedBy = ownerToken.Value,
            },
            transaction).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(SqlException ex)
    {
        return ex.Number is 2627 or 2601;
    }

}
