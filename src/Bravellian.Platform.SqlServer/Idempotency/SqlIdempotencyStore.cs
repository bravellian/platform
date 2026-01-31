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

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Bravellian.Platform.Idempotency;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal sealed class SqlIdempotencyStore : IIdempotencyStore, IIdempotencyCleanupStore
{
    private const int StoredProcedureMissing = 2812;
    private const int TableMissing = 208;
    private const byte StatusFailed = 0;
    private const byte StatusCompleted = 2;
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeSpan lockDuration;
    private readonly Func<string, TimeSpan>? lockDurationProvider;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlIdempotencyStore> logger;
    private readonly OwnerToken ownerToken;

    public SqlIdempotencyStore(
        IOptions<SqlIdempotencyOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlIdempotencyStore> logger)
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

        if (!IsValidLockDuration(opts.LockDuration))
        {
            throw new ArgumentOutOfRangeException(nameof(options), opts.LockDuration, "LockDuration must be positive or Timeout.InfiniteTimeSpan.");
        }

        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        lockDuration = opts.LockDuration;
        lockDurationProvider = opts.LockDurationProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
        ownerToken = OwnerToken.GenerateNew();
    }

    public async Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        var now = timeProvider.GetUtcNow();
        var duration = lockDurationProvider?.Invoke(key) ?? lockDuration;
        if (!IsValidLockDuration(duration))
        {
            throw new InvalidOperationException("Lock duration must be positive or Timeout.InfiniteTimeSpan.");
        }

        var lockedUntil = duration == Timeout.InfiniteTimeSpan ? (DateTimeOffset?)null : now.Add(duration);

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var record = await GetRecordForUpdateAsync(connection, transaction, key).ConfigureAwait(false);
            if (record == null)
            {
                await InsertRecordAsync(connection, transaction, key, now, lockedUntil).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (record.Status == IdempotencyStatus.Completed)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var existingLock = record.LockedUntil;
            if (record.Status == IdempotencyStatus.InProgress
                && record.LockedBy != ownerToken.Value
                && IsLocked(existingLock, now))
            {
                logger.LogDebug("Idempotency key '{Key}' is locked until {LockedUntil}.", key, existingLock);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await MarkInProgressAsync(connection, transaction, key, now, lockedUntil).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        var now = timeProvider.GetUtcNow();
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.ExecuteAsync(
            $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                LockedUntil = NULL,
                LockedBy = NULL,
                CompletedAt = @CompletedAt,
                UpdatedAt = @UpdatedAt
            WHERE IdempotencyKey = @Key;
            """,
            new
            {
                Status = (byte)IdempotencyStatus.Completed,
                CompletedAt = now,
                UpdatedAt = now,
                Key = key,
            }).ConfigureAwait(false);

        if (rows == 0)
        {
            await InsertCompletionAsync(connection, key, now).ConfigureAwait(false);
        }
    }

    public async Task FailAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        var now = timeProvider.GetUtcNow();
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.ExecuteAsync(
            $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                LockedUntil = NULL,
                LockedBy = NULL,
                UpdatedAt = @UpdatedAt,
                FailureCount = FailureCount + 1
            WHERE IdempotencyKey = @Key;
            """,
            new
            {
                Status = (byte)IdempotencyStatus.Failed,
                UpdatedAt = now,
                Key = key,
            }).ConfigureAwait(false);

        if (rows == 0)
        {
            await InsertFailureAsync(connection, key, now).ConfigureAwait(false);
        }
    }

    private async Task<IdempotencyRecord?> GetRecordForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key)
    {
        var sql = $"""
            SELECT Status AS StatusValue, LockedUntil, LockedBy
            FROM [{schemaName}].[{tableName}] WITH (UPDLOCK, HOLDLOCK)
            WHERE IdempotencyKey = @Key;
            """;

        return await connection.QuerySingleOrDefaultAsync<IdempotencyRecord>(
            sql,
            new { Key = key },
            transaction).ConfigureAwait(false);
    }

    private Task<int> InsertRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        DateTimeOffset now,
        DateTimeOffset? lockedUntil)
    {
        var sql = $"""
            INSERT INTO [{schemaName}].[{tableName}] (
                IdempotencyKey,
                Status,
                LockedUntil,
                LockedBy,
                FailureCount,
                CreatedAt,
                UpdatedAt)
            VALUES (
                @Key,
                @Status,
                @LockedUntil,
                @LockedBy,
                0,
                @CreatedAt,
                @UpdatedAt);
            """;

        return connection.ExecuteAsync(
            sql,
            new
            {
                Key = key,
                Status = (byte)IdempotencyStatus.InProgress,
                LockedUntil = lockedUntil,
                LockedBy = ownerToken.Value,
                CreatedAt = now,
                UpdatedAt = now,
            },
            transaction);
    }

    private Task<int> MarkInProgressAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        DateTimeOffset now,
        DateTimeOffset? lockedUntil)
    {
        var sql = $"""
            UPDATE [{schemaName}].[{tableName}]
            SET Status = @Status,
                LockedUntil = @LockedUntil,
                LockedBy = @LockedBy,
                UpdatedAt = @UpdatedAt
            WHERE IdempotencyKey = @Key;
            """;

        return connection.ExecuteAsync(
            sql,
            new
            {
                Status = (byte)IdempotencyStatus.InProgress,
                LockedUntil = lockedUntil,
                LockedBy = ownerToken.Value,
                UpdatedAt = now,
                Key = key,
            },
            transaction);
    }

    private Task<int> InsertCompletionAsync(SqlConnection connection, string key, DateTimeOffset now)
    {
        var sql = $"""
            INSERT INTO [{schemaName}].[{tableName}] (
                IdempotencyKey,
                Status,
                FailureCount,
                CreatedAt,
                UpdatedAt,
                CompletedAt)
            VALUES (
                @Key,
                @Status,
                0,
                @CreatedAt,
                @UpdatedAt,
                @CompletedAt);
            """;

        return connection.ExecuteAsync(
            sql,
            new
            {
                Key = key,
                Status = (byte)IdempotencyStatus.Completed,
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now,
            });
    }

    private Task<int> InsertFailureAsync(SqlConnection connection, string key, DateTimeOffset now)
    {
        var sql = $"""
            INSERT INTO [{schemaName}].[{tableName}] (
                IdempotencyKey,
                Status,
                FailureCount,
                CreatedAt,
                UpdatedAt)
            VALUES (
                @Key,
                @Status,
                1,
                @CreatedAt,
                @UpdatedAt);
            """;

        return connection.ExecuteAsync(
            sql,
            new
            {
                Key = key,
                Status = (byte)IdempotencyStatus.Failed,
                CreatedAt = now,
                UpdatedAt = now,
            });
    }

    private sealed record IdempotencyRecord(byte StatusValue, DateTimeOffset? LockedUntil, Guid? LockedBy)
    {
        public IdempotencyStatus Status => (IdempotencyStatus)StatusValue;
    }

    private enum IdempotencyStatus : byte
    {
        Failed = 0,
        InProgress = 1,
        Completed = 2
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Uses validated schema and table names from options.")]
    public async Task<int> CleanupAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken)
    {
        if (retentionPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionPeriod), retentionPeriod, "Retention period must be greater than zero.");
        }

        var retentionSeconds = (int)retentionPeriod.TotalSeconds;
        var proc = $"[{schemaName}].[{tableName}_Cleanup]";

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using (var command = new SqlCommand($"EXEC {proc} @RetentionSeconds", connection))
            {
                command.Parameters.AddWithValue("@RetentionSeconds", retentionSeconds);
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result != null && result != DBNull.Value
                    ? Convert.ToInt32(result, CultureInfo.InvariantCulture)
                    : 0;
            }
        }
        catch (SqlException ex) when (ex.Number == StoredProcedureMissing)
        {
            // fall back to direct delete
        }

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                DELETE FROM [{schemaName}].[{tableName}]
                WHERE (Status = @StatusCompleted OR Status = @StatusFailed)
                  AND (
                        (CompletedAt IS NOT NULL AND CompletedAt < DATEADD(SECOND, -@RetentionSeconds, SYSUTCDATETIME()))
                        OR (CompletedAt IS NULL AND UpdatedAt < DATEADD(SECOND, -@RetentionSeconds, SYSUTCDATETIME()))
                      );
                """;

            var deleted = await connection.ExecuteAsync(
                sql,
                new
                {
                    StatusCompleted = StatusCompleted,
                    StatusFailed = StatusFailed,
                    RetentionSeconds = retentionSeconds,
                }).ConfigureAwait(false);

            return deleted;
        }
        catch (SqlException ex) when (ex.Number == TableMissing)
        {
            logger.LogWarning(
                "Idempotency cleanup skipped because table [{SchemaName}].[{TableName}] is missing.",
                schemaName,
                tableName);
            return 0;
        }
    }

    private static bool IsValidLockDuration(TimeSpan duration)
    {
        return duration == Timeout.InfiniteTimeSpan || duration > TimeSpan.Zero;
    }

    private static bool IsLocked(DateTimeOffset? lockedUntil, DateTimeOffset now)
    {
        return lockedUntil == null || lockedUntil > now;
    }
}
