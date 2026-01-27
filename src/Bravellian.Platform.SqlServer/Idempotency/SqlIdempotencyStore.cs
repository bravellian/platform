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
using Bravellian.Platform.Idempotency;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal sealed class SqlIdempotencyStore : IIdempotencyStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeSpan lockDuration;
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

        if (opts.LockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), opts.LockDuration, "LockDuration must be positive.");
        }

        connectionString = opts.ConnectionString;
        schemaName = opts.SchemaName;
        tableName = opts.TableName;
        lockDuration = opts.LockDuration;
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
        var lockedUntil = now.Add(lockDuration);

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var record = await GetRecordForUpdateAsync(connection, transaction, key).ConfigureAwait(false);
        if (record == null)
        {
            await InsertRecordAsync(connection, transaction, key, now, lockedUntil).ConfigureAwait(false);
            transaction.Commit();
            return true;
        }

        if (record.Status == IdempotencyStatus.Completed)
        {
            transaction.Commit();
            return false;
        }

        if (record.Status == IdempotencyStatus.InProgress
            && record.LockedUntil is DateTimeOffset existingLock
            && existingLock > now
            && record.LockedBy != ownerToken.Value)
        {
            logger.LogDebug("Idempotency key '{Key}' is locked until {LockedUntil}.", key, existingLock);
            transaction.Commit();
            return false;
        }

        await MarkInProgressAsync(connection, transaction, key, now, lockedUntil).ConfigureAwait(false);
        transaction.Commit();
        return true;
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

    private Task InsertRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        DateTimeOffset now,
        DateTimeOffset lockedUntil)
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

    private Task MarkInProgressAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        DateTimeOffset now,
        DateTimeOffset lockedUntil)
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

    private Task InsertCompletionAsync(SqlConnection connection, string key, DateTimeOffset now)
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

    private Task InsertFailureAsync(SqlConnection connection, string key, DateTimeOffset now)
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
}
