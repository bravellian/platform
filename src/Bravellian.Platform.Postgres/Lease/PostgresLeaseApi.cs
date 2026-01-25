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
using Npgsql;

namespace Bravellian.Platform;
/// <summary>
/// Provides data access operations for the lease functionality.
/// </summary>
public sealed class PostgresLeaseApi
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string leaseTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresLeaseApi"/> class.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name for the lease table.</param>
    public PostgresLeaseApi(string connectionString, string schemaName = "infra")
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        this.schemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        leaseTable = PostgresSqlHelper.Qualify(this.schemaName, "Lease");
    }

    /// <summary>
    /// Attempts to acquire a lease.
    /// </summary>
    public async Task<LeaseAcquireResult> AcquireAsync(
        string name,
        string owner,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var now = await connection.ExecuteScalarAsync<DateTime>(
                "SELECT CURRENT_TIMESTAMP;",
                transaction).ConfigureAwait(false);
            var nowUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);

            await connection.ExecuteAsync(
                $"""
                INSERT INTO {leaseTable} ("Name", "Owner", "LeaseUntilUtc", "LastGrantedUtc")
                VALUES (@Name, NULL, NULL, NULL)
                ON CONFLICT ("Name") DO NOTHING;
                """,
                new { Name = name },
                transaction).ConfigureAwait(false);

            var leaseUntilUtc = await connection.ExecuteScalarAsync<DateTime?>(
                $"""
                UPDATE {leaseTable}
                SET "Owner" = @Owner,
                    "LeaseUntilUtc" = @LeaseUntilUtc,
                    "LastGrantedUtc" = @Now
                WHERE "Name" = @Name
                    AND ("Owner" IS NULL OR "LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= @Now)
                RETURNING "LeaseUntilUtc";
                """,
                new
                {
                    Name = name,
                    Owner = owner,
                    Now = nowUtc,
                    LeaseUntilUtc = nowUtc.AddSeconds(leaseSeconds),
                },
                transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new LeaseAcquireResult(leaseUntilUtc.HasValue, nowUtc, leaseUntilUtc);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Attempts to renew a lease.
    /// </summary>
    public async Task<LeaseRenewResult> RenewAsync(
        string name,
        string owner,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var now = await connection.ExecuteScalarAsync<DateTime>("SELECT CURRENT_TIMESTAMP;").ConfigureAwait(false);
        var nowUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);

        var leaseUntilUtc = await connection.ExecuteScalarAsync<DateTime?>(
            $"""
            UPDATE {leaseTable}
            SET "LeaseUntilUtc" = @LeaseUntilUtc,
                "LastGrantedUtc" = @Now
            WHERE "Name" = @Name
                AND "Owner" = @Owner
                AND "LeaseUntilUtc" > @Now
            RETURNING "LeaseUntilUtc";
            """,
            new
            {
                Name = name,
                Owner = owner,
                Now = nowUtc,
                LeaseUntilUtc = nowUtc.AddSeconds(leaseSeconds),
            }).ConfigureAwait(false);

        return new LeaseRenewResult(leaseUntilUtc.HasValue, nowUtc, leaseUntilUtc);
    }
}
