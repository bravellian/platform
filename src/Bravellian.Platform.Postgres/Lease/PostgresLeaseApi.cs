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
            var sql = $"""
                WITH now_cte AS (
                    SELECT CURRENT_TIMESTAMP AS "now"
                ),
                ins AS (
                    INSERT INTO {leaseTable} ("Name", "Owner", "LeaseUntilUtc", "LastGrantedUtc")
                    VALUES (@Name, NULL, NULL, NULL)
                    ON CONFLICT ("Name") DO NOTHING
                ),
                upd AS (
                    UPDATE {leaseTable}
                    SET "Owner" = @Owner,
                        "LeaseUntilUtc" = (SELECT "now" FROM now_cte) + (@LeaseSeconds || ' seconds')::interval,
                        "LastGrantedUtc" = (SELECT "now" FROM now_cte)
                    WHERE "Name" = @Name
                        AND ("Owner" IS NULL OR "LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= (SELECT "now" FROM now_cte))
                    RETURNING "LeaseUntilUtc"
                )
                SELECT
                    EXISTS (SELECT 1 FROM upd) AS "acquired",
                    (SELECT "now" FROM now_cte) AS "serverUtcNow",
                    (SELECT "LeaseUntilUtc" FROM upd) AS "leaseUntilUtc";
                """;

            var result = await connection.QuerySingleAsync<LeaseAcquireResult>(
                sql,
                new { Name = name, Owner = owner, LeaseSeconds = leaseSeconds },
                transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
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

        var sql = $"""
            WITH now_cte AS (
                SELECT CURRENT_TIMESTAMP AS "now"
            ),
            upd AS (
                UPDATE {leaseTable}
                SET "LeaseUntilUtc" = (SELECT "now" FROM now_cte) + (@LeaseSeconds || ' seconds')::interval,
                    "LastGrantedUtc" = (SELECT "now" FROM now_cte)
                WHERE "Name" = @Name
                    AND "Owner" = @Owner
                    AND "LeaseUntilUtc" > (SELECT "now" FROM now_cte)
                RETURNING "LeaseUntilUtc"
            )
            SELECT
                EXISTS (SELECT 1 FROM upd) AS "renewed",
                (SELECT "now" FROM now_cte) AS "serverUtcNow",
                (SELECT "LeaseUntilUtc" FROM upd) AS "leaseUntilUtc";
            """;

        return await connection.QuerySingleAsync<LeaseRenewResult>(
            sql,
            new { Name = name, Owner = owner, LeaseSeconds = leaseSeconds }).ConfigureAwait(false);
    }
}
