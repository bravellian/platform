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
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Semaphore;
/// <summary>
/// SQL Server implementation of the semaphore service.
/// </summary>
internal sealed class SqlSemaphoreService : ISemaphoreService
{
    private readonly SemaphoreOptions options;
    private readonly ILogger<SqlSemaphoreService> logger;
    private readonly string serverName;
    private readonly string databaseName;

    public SqlSemaphoreService(
        IOptions<SemaphoreOptions> options,
        ILogger<SqlSemaphoreService> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        (serverName, databaseName) = ParseConnectionInfo(this.options.ConnectionString);
    }

    public async Task<SemaphoreAcquireResult> TryAcquireAsync(
        string name,
        int ttlSeconds,
        string ownerId,
        CancellationToken cancellationToken)
    {
        return await TryAcquireAsync(name, ttlSeconds, ownerId, null, cancellationToken).ConfigureAwait(false);
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
            using var connection = new SqlConnection(options.ConnectionString);
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
                $"[{options.SchemaName}].[Semaphore_Acquire]",
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
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error acquiring semaphore '{Name}' for owner '{OwnerId}' on {Server}/{Database} (Schema {Schema})",
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
            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@Token", token, DbType.Guid);
            parameters.Add("@TtlSeconds", ttlSeconds, DbType.Int32);
            parameters.Add("@Renewed", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@ExpiresAtUtc", dbType: DbType.DateTime2, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{options.SchemaName}].[Semaphore_Renew]",
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
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error renewing semaphore '{Name}' token '{Token}' on {Server}/{Database} (Schema {Schema})",
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
            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@Token", token, DbType.Guid);
            parameters.Add("@Released", dbType: DbType.Boolean, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{options.SchemaName}].[Semaphore_Release]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            var released = parameters.Get<bool>("@Released");
            return released ? SemaphoreReleaseResult.Released() : SemaphoreReleaseResult.NotFound();
        }
        catch (SqlException ex)
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
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error releasing semaphore '{Name}' token '{Token}' on {Server}/{Database} (Schema {Schema})",
                name,
                token,
                serverName,
                databaseName,
                options.SchemaName);
            return SemaphoreReleaseResult.Unavailable();
        }
    }

    public async Task<int> ReapExpiredAsync(CancellationToken cancellationToken)
    {
        return await ReapExpiredAsync(null, 1000, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ReapExpiredAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        return await ReapExpiredAsync(name, 1000, cancellationToken).ConfigureAwait(false);
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
            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@Name", name, DbType.String);
            parameters.Add("@MaxRows", maxRows, DbType.Int32);
            parameters.Add("@DeletedCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                $"[{options.SchemaName}].[Semaphore_Reap]",
                parameters,
                commandType: CommandType.StoredProcedure).ConfigureAwait(false);

            return parameters.Get<int>("@DeletedCount");
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

    public async Task EnsureExistsAsync(
        string name,
        int limit,
        CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(limit, options.MaxLimit);

        try
        {
            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""
                IF NOT EXISTS (SELECT 1 FROM [{options.SchemaName}].[Semaphore] WHERE [Name] = @Name)
                BEGIN
                    INSERT INTO [{options.SchemaName}].[Semaphore] ([Name], [Limit], [NextFencingCounter], [UpdatedUtc])
                    VALUES (@Name, @Limit, 1, SYSUTCDATETIME())
                END
                """;

            await connection.ExecuteAsync(
                sql,
                new { Name = name, Limit = limit }).ConfigureAwait(false);
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

    public async Task UpdateLimitAsync(
        string name,
        int newLimit,
        CancellationToken cancellationToken)
    {
        await UpdateLimitAsync(name, newLimit, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateLimitAsync(
        string name,
        int newLimit,
        bool ensureIfMissing,
        CancellationToken cancellationToken)
    {
        SemaphoreValidator.ValidateName(name);
        SemaphoreValidator.ValidateLimit(newLimit, options.MaxLimit);

        try
        {
            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (ensureIfMissing)
            {
                var sql = $"""
                    MERGE [{options.SchemaName}].[Semaphore] AS target
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
                    UPDATE [{options.SchemaName}].[Semaphore]
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
            var builder = new SqlConnectionStringBuilder(cs);
            return (builder.DataSource ?? "unknown-server", builder.InitialCatalog ?? "unknown-database");
        }
        catch
        {
            return ("unknown-server", "unknown-database");
        }
    }
}
