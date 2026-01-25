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

using System.Linq;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// PostgreSQL implementation of the semaphore service.
/// </summary>
internal sealed class PostgresSemaphoreService : ISemaphoreService
{
    private readonly PostgresSemaphoreOptions options;
    private readonly ILogger<PostgresSemaphoreService> logger;
    private readonly string serverName;
    private readonly string databaseName;
    private readonly string semaphoreTable;
    private readonly string leaseTable;

    public PostgresSemaphoreService(
        IOptions<PostgresSemaphoreOptions> options,
        ILogger<PostgresSemaphoreService> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        (serverName, databaseName) = ParseConnectionInfo(this.options.ConnectionString);
        semaphoreTable = PostgresSqlHelper.Qualify(this.options.SchemaName, "Semaphore");
        leaseTable = PostgresSqlHelper.Qualify(this.options.SchemaName, "SemaphoreLease");
    }

    public Task<SemaphoreAcquireResult> TryAcquireAsync(
        string name,
        int ttlSeconds,
        string ownerId,
        CancellationToken cancellationToken)
    {
        return TryAcquireAsync(name, ttlSeconds, ownerId, null, cancellationToken);
    }

    public async Task<SemaphoreAcquireResult> TryAcquireAsync(
        string name,
        int ttlSeconds,
        string ownerId,
        string? clientRequestId,
        CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateOwnerId(ownerId);
        SemaphoreValidator.ValidateTtl(ttlSeconds, options.MinTtlSeconds, options.MaxTtlSeconds);

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var now = await connection.ExecuteScalarAsync<DateTime>(
                    "SELECT CURRENT_TIMESTAMP;",
                    transaction).ConfigureAwait(false);
                var nowUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);

                var limit = await connection.ExecuteScalarAsync<int?>(
                    $"""
                    SELECT "Limit"
                    FROM {semaphoreTable}
                    WHERE "Name" = @Name
                    FOR UPDATE;
                    """,
                    new { Name = name },
                    transaction).ConfigureAwait(false);

                if (limit == null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return SemaphoreAcquireResult.NotAcquired();
                }

                if (!string.IsNullOrEmpty(clientRequestId))
                {
                    var existing = await connection.QueryFirstOrDefaultAsync<(Guid Token, long Fencing, DateTime LeaseUntilUtc)>(
                        $"""
                        SELECT "Token", "Fencing", "LeaseUntilUtc"
                        FROM {leaseTable}
                        WHERE "Name" = @Name
                            AND "ClientRequestId" = @ClientRequestId
                            AND "LeaseUntilUtc" > @Now
                        LIMIT 1;
                        """,
                        new { Name = name, ClientRequestId = clientRequestId, Now = nowUtc },
                        transaction).ConfigureAwait(false);

                    if (existing != default)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return SemaphoreAcquireResult.Acquired(existing.Token, existing.Fencing, existing.LeaseUntilUtc);
                    }
                }

                await connection.ExecuteAsync(
                    $"""
                    WITH expired AS (
                        SELECT ctid
                        FROM {leaseTable}
                        WHERE "Name" = @Name AND "LeaseUntilUtc" <= @Now
                        ORDER BY "LeaseUntilUtc"
                        LIMIT 10
                    )
                    DELETE FROM {leaseTable}
                    WHERE ctid IN (SELECT ctid FROM expired);
                    """,
                    new { Name = name, Now = nowUtc },
                    transaction).ConfigureAwait(false);

                var activeCount = await connection.ExecuteScalarAsync<int>(
                    $"""
                    SELECT COUNT(*)
                    FROM {leaseTable}
                    WHERE "Name" = @Name AND "LeaseUntilUtc" > @Now;
                    """,
                    new { Name = name, Now = nowUtc },
                    transaction).ConfigureAwait(false);

                if (activeCount >= limit.Value)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return SemaphoreAcquireResult.NotAcquired();
                }

                var until = nowUtc.AddSeconds(ttlSeconds);
                var token = Guid.NewGuid();

                var fencing = await connection.ExecuteScalarAsync<long>(
                    $"""
                    UPDATE {semaphoreTable}
                    SET "NextFencingCounter" = "NextFencingCounter" + 1,
                        "UpdatedUtc" = @Now
                    WHERE "Name" = @Name
                    RETURNING "NextFencingCounter" - 1;
                    """,
                    new { Name = name, Now = nowUtc },
                    transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    $"""
                    INSERT INTO {leaseTable}
                        ("Name", "Token", "Fencing", "OwnerId", "LeaseUntilUtc", "CreatedUtc", "ClientRequestId")
                    VALUES
                        (@Name, @Token, @Fencing, @OwnerId, @LeaseUntilUtc, @CreatedUtc, @ClientRequestId);
                    """,
                    new
                    {
                        Name = name,
                        Token = token,
                        Fencing = fencing,
                        OwnerId = ownerId,
                        LeaseUntilUtc = until,
                        CreatedUtc = nowUtc,
                        ClientRequestId = clientRequestId,
                    },
                    transaction).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return SemaphoreAcquireResult.Acquired(token, fencing, until);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to acquire semaphore '{Name}' for owner '{OwnerId}' on {Server}/{Database} (Schema {Schema})",
                name,
                ownerId,
                serverName,
                databaseName,
                options.SchemaName);
            return SemaphoreAcquireResult.Unavailable();
        }
    }

    public async Task<SemaphoreRenewResult> RenewAsync(
        string name,
        Guid token,
        int ttlSeconds,
        CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateTtl(ttlSeconds, options.MinTtlSeconds, options.MaxTtlSeconds);

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var now = await connection.ExecuteScalarAsync<DateTime>("SELECT CURRENT_TIMESTAMP;").ConfigureAwait(false);
            var nowUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
            var until = nowUtc.AddSeconds(ttlSeconds);

            var currentExpiry = await connection.ExecuteScalarAsync<DateTime?>(
                $"""
                SELECT "LeaseUntilUtc"
                FROM {leaseTable}
                WHERE "Name" = @Name AND "Token" = @Token;
                """,
                new { Name = name, Token = token }).ConfigureAwait(false);

            if (currentExpiry == null || currentExpiry <= nowUtc)
            {
                return SemaphoreRenewResult.Lost();
            }

            var newExpiry = until > currentExpiry ? until : currentExpiry.Value;

            await connection.ExecuteAsync(
                $"""
                UPDATE {leaseTable}
                SET "LeaseUntilUtc" = @LeaseUntilUtc,
                    "RenewedUtc" = @RenewedUtc
                WHERE "Name" = @Name AND "Token" = @Token;
                """,
                new
                {
                    Name = name,
                    Token = token,
                    LeaseUntilUtc = newExpiry,
                    RenewedUtc = nowUtc,
                }).ConfigureAwait(false);

            return SemaphoreRenewResult.Renewed(newExpiry);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to renew semaphore '{Name}' token '{Token}' on {Server}/{Database} (Schema {Schema})",
                name,
                token,
                serverName,
                databaseName,
                options.SchemaName);
            return SemaphoreRenewResult.Unavailable();
        }
    }

    public async Task<SemaphoreReleaseResult> ReleaseAsync(
        string name,
        Guid token,
        CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rows = await connection.ExecuteAsync(
                $"""
                DELETE FROM {leaseTable}
                WHERE "Name" = @Name AND "Token" = @Token;
                """,
                new { Name = name, Token = token }).ConfigureAwait(false);

            return rows > 0 ? SemaphoreReleaseResult.Released() : SemaphoreReleaseResult.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to release semaphore '{Name}' token '{Token}' on {Server}/{Database} (Schema {Schema})",
                name,
                token,
                serverName,
                databaseName,
                options.SchemaName);
            return SemaphoreReleaseResult.Unavailable();
        }
    }

    public Task<int> ReapExpiredAsync(CancellationToken cancellationToken)
    {
        return ReapExpiredAsync(null, 1000, cancellationToken);
    }

    public Task<int> ReapExpiredAsync(string? name, CancellationToken cancellationToken)
    {
        return ReapExpiredAsync(name, 1000, cancellationToken);
    }

    public async Task<int> ReapExpiredAsync(
        string? name,
        int maxRows,
        CancellationToken cancellationToken)
    {
        if (name != null)
        {
            SemaphoreValidator.ValidateName(name);
        }

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = name == null
                ? $"""
                WITH expired AS (
                    SELECT ctid
                    FROM {leaseTable}
                    WHERE "LeaseUntilUtc" <= CURRENT_TIMESTAMP
                    LIMIT @MaxRows
                )
                DELETE FROM {leaseTable}
                WHERE ctid IN (SELECT ctid FROM expired)
                RETURNING 1;
                """
                : $"""
                WITH expired AS (
                    SELECT ctid
                    FROM {leaseTable}
                    WHERE "Name" = @Name AND "LeaseUntilUtc" <= CURRENT_TIMESTAMP
                    LIMIT @MaxRows
                )
                DELETE FROM {leaseTable}
                WHERE ctid IN (SELECT ctid FROM expired)
                RETURNING 1;
                """;

            var deleted = await connection.QueryAsync<int>(
                sql,
                new { Name = name, MaxRows = maxRows }).ConfigureAwait(false);

            return deleted.Count();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to reap expired semaphore leases for '{Name}' on {Server}/{Database} (Schema {Schema})",
                name ?? "all",
                serverName,
                databaseName,
                options.SchemaName);
            return 0;
        }
    }

    public async Task EnsureExistsAsync(string name, int limit, CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(limit, options.MaxLimit);

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                INSERT INTO {semaphoreTable} ("Name", "Limit", "NextFencingCounter", "UpdatedUtc")
                VALUES (@Name, @Limit, 1, CURRENT_TIMESTAMP)
                ON CONFLICT ("Name") DO NOTHING;
                """;

            await connection.ExecuteAsync(sql, new { Name = name, Limit = limit }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to ensure semaphore '{Name}' exists with limit {Limit} on {Server}/{Database} (Schema {Schema})",
                name,
                limit,
                serverName,
                databaseName,
                options.SchemaName);
            throw;
        }
    }

    public Task UpdateLimitAsync(string name, int newLimit, CancellationToken cancellationToken)
    {
        return UpdateLimitAsync(name, newLimit, false, cancellationToken);
    }

    public async Task UpdateLimitAsync(string name, int newLimit, bool ensureIfMissing, CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(newLimit, options.MaxLimit);

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (ensureIfMissing)
            {
                var sql = $"""
                    INSERT INTO {semaphoreTable} ("Name", "Limit", "NextFencingCounter", "UpdatedUtc")
                    VALUES (@Name, @NewLimit, 1, CURRENT_TIMESTAMP)
                    ON CONFLICT ("Name") DO UPDATE
                    SET "Limit" = EXCLUDED."Limit",
                        "UpdatedUtc" = CURRENT_TIMESTAMP;
                    """;

                await connection.ExecuteAsync(sql, new { Name = name, NewLimit = newLimit }).ConfigureAwait(false);
            }
            else
            {
                var sql = $"""
                    UPDATE {semaphoreTable}
                    SET "Limit" = @NewLimit, "UpdatedUtc" = CURRENT_TIMESTAMP
                    WHERE "Name" = @Name;
                    """;

                var rowsAffected = await connection.ExecuteAsync(
                    sql,
                    new { Name = name, NewLimit = newLimit }).ConfigureAwait(false);

                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException($"Semaphore '{name}' does not exist.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update semaphore '{Name}' limit to {NewLimit} on {Server}/{Database} (Schema {Schema})",
                name,
                newLimit,
                serverName,
                databaseName,
                options.SchemaName);
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
