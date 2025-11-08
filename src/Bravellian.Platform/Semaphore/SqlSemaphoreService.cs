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

namespace Bravellian.Platform.Semaphore;

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// SQL Server implementation of the semaphore service.
/// </summary>
internal sealed class SqlSemaphoreService : ISemaphoreService
{
    private readonly SemaphoreOptions options;
    private readonly ILogger<SqlSemaphoreService> logger;

    public SqlSemaphoreService(
        IOptions<SemaphoreOptions> options,
        ILogger<SqlSemaphoreService> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<SemaphoreAcquireResult> TryAcquireAsync(
        string name,
        int ttlSeconds,
        string ownerId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateOwnerId(ownerId);
        SemaphoreValidator.ValidateTtl(ttlSeconds, this.options.MinTtlSeconds, this.options.MaxTtlSeconds);

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@OwnerId", ownerId, DbType.String);
            parameters.Add("@TtlSeconds", ttlSeconds, DbType.Int32);
            parameters.Add("@ClientRequestId", clientRequestId, DbType.String);
            parameters.Add("@Acquired", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@Token", dbType: DbType.Guid, direction: ParameterDirection.Output);
            parameters.Add("@Fencing", dbType: DbType.Int64, direction: ParameterDirection.Output);
            parameters.Add("@ExpiresAtUtc", dbType: DbType.DateTime2, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{this.options.SchemaName}].[Semaphore_Acquire]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            var acquired = parameters.Get<bool>("@Acquired");
            if (!acquired)
            {
                return SemaphoreAcquireResult.NotAcquired();
            }

            var token = parameters.Get<Guid>("@Token");
            var fencing = parameters.Get<long>("@Fencing");
            var expiresAtUtc = parameters.Get<DateTime>("@ExpiresAtUtc");

            return SemaphoreAcquireResult.Acquired(token, fencing, expiresAtUtc);
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, "Failed to acquire semaphore '{Name}' for owner '{OwnerId}'", name, ownerId);
            return SemaphoreAcquireResult.Unavailable();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Unexpected error acquiring semaphore '{Name}' for owner '{OwnerId}'", name, ownerId);
            return SemaphoreAcquireResult.Unavailable();
        }
    }

    public async Task<SemaphoreRenewResult> RenewAsync(
        string name,
        Guid token,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateTtl(ttlSeconds, this.options.MinTtlSeconds, this.options.MaxTtlSeconds);

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@Token", token, DbType.Guid);
            parameters.Add("@TtlSeconds", ttlSeconds, DbType.Int32);
            parameters.Add("@Renewed", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@ExpiresAtUtc", dbType: DbType.DateTime2, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{this.options.SchemaName}].[Semaphore_Renew]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            var renewed = parameters.Get<bool>("@Renewed");
            if (!renewed)
            {
                return SemaphoreRenewResult.Lost();
            }

            var expiresAtUtc = parameters.Get<DateTime>("@ExpiresAtUtc");
            return SemaphoreRenewResult.Renewed(expiresAtUtc);
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, "Failed to renew semaphore '{Name}' token '{Token}'", name, token);
            return SemaphoreRenewResult.Unavailable();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Unexpected error renewing semaphore '{Name}' token '{Token}'", name, token);
            return SemaphoreRenewResult.Unavailable();
        }
    }

    public async Task<SemaphoreReleaseResult> ReleaseAsync(
        string name,
        Guid token,
        CancellationToken cancellationToken = default)
    {
        SemaphoreValidator.ValidateName(name);

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@Token", token, DbType.Guid);
            parameters.Add("@Released", dbType: DbType.Boolean, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{this.options.SchemaName}].[Semaphore_Release]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            var released = parameters.Get<bool>("@Released");
            return released ? SemaphoreReleaseResult.Released() : SemaphoreReleaseResult.NotFound();
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, "Failed to release semaphore '{Name}' token '{Token}'", name, token);
            return SemaphoreReleaseResult.Unavailable();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Unexpected error releasing semaphore '{Name}' token '{Token}'", name, token);
            return SemaphoreReleaseResult.Unavailable();
        }
    }

    public async Task<int> ReapExpiredAsync(
        string? name = null,
        int maxRows = 1000,
        CancellationToken cancellationToken = default)
    {
        if (name != null)
        {
            SemaphoreValidator.ValidateName(name);
        }

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@MaxRows", maxRows, DbType.Int32);
            parameters.Add("@DeletedCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{this.options.SchemaName}].[Semaphore_Reap]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            return parameters.Get<int>("@DeletedCount");
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to reap expired semaphore leases for '{Name}'", name ?? "all");
            return 0;
        }
    }

    public async Task EnsureExistsAsync(
        string name,
        int limit,
        CancellationToken cancellationToken = default)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(limit, this.options.MaxLimit);

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                IF NOT EXISTS (SELECT 1 FROM [{this.options.SchemaName}].[Semaphore] WHERE [Name] = @Name)
                BEGIN
                    INSERT INTO [{this.options.SchemaName}].[Semaphore] ([Name], [Limit], [NextFencingCounter], [UpdatedUtc])
                    VALUES (@Name, @Limit, 1, SYSUTCDATETIME())
                END
                """;

            await connection.ExecuteAsync(
                sql,
                new { Name = name, Limit = limit }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to ensure semaphore '{Name}' exists with limit {Limit}", name, limit);
            throw;
        }
    }

    public async Task UpdateLimitAsync(
        string name,
        int newLimit,
        bool ensureIfMissing = false,
        CancellationToken cancellationToken = default)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(newLimit, this.options.MaxLimit);

        try
        {
            using var connection = new SqlConnection(this.options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (ensureIfMissing)
            {
                var sql = $"""
                    MERGE [{this.options.SchemaName}].[Semaphore] AS target
                    USING (SELECT @Name AS [Name], @NewLimit AS [Limit]) AS source
                    ON (target.[Name] = source.[Name])
                    WHEN MATCHED THEN
                        UPDATE SET [Limit] = source.[Limit], [UpdatedUtc] = SYSUTCDATETIME()
                    WHEN NOT MATCHED THEN
                        INSERT ([Name], [Limit], [NextFencingCounter], [UpdatedUtc])
                        VALUES (source.[Name], source.[Limit], 1, SYSUTCDATETIME());
                    """;

                await connection.ExecuteAsync(
                    sql,
                    new { Name = name, NewLimit = newLimit }).ConfigureAwait(false);
            }
            else
            {
                var sql = $"""
                    UPDATE [{this.options.SchemaName}].[Semaphore]
                    SET [Limit] = @NewLimit, [UpdatedUtc] = SYSUTCDATETIME()
                    WHERE [Name] = @Name
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
            this.logger.LogError(ex, "Failed to update semaphore '{Name}' limit to {NewLimit}", name, newLimit);
            throw;
        }
    }
}
