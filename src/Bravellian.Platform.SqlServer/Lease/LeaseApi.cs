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
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform;
/// <summary>
/// Provides data access operations for the lease functionality.
/// </summary>
public sealed class LeaseApi
{
    private readonly string connectionString;
    private readonly string schemaName;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeaseApi"/> class.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name for the lease table.</param>
    public LeaseApi(string connectionString, string schemaName = "infra")
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        this.schemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
    }

    /// <summary>
    /// Attempts to acquire a lease.
    /// </summary>
    /// <param name="name">The lease name.</param>
    /// <param name="owner">The owner identifier.</param>
    /// <param name="leaseSeconds">The lease duration in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the acquire operation.</returns>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Uses stored procedure name derived from configured schema.")]
    public async Task<LeaseAcquireResult> AcquireAsync(
        string name,
        string owner,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = new SqlCommand($"[{schemaName}].[Lease_Acquire]", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Owner", owner);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);

        var acquiredParam = new SqlParameter("@Acquired", SqlDbType.Bit) { Direction = ParameterDirection.Output };
        var serverUtcNowParam = new SqlParameter("@ServerUtcNow", SqlDbType.DateTime2) { Direction = ParameterDirection.Output };
        var leaseUntilUtcParam = new SqlParameter("@LeaseUntilUtc", SqlDbType.DateTime2) { Direction = ParameterDirection.Output };

        command.Parameters.Add(acquiredParam);
        command.Parameters.Add(serverUtcNowParam);
        command.Parameters.Add(leaseUntilUtcParam);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var acquired = (bool)acquiredParam.Value;
        var serverUtcNow = (DateTime)serverUtcNowParam.Value;
        var leaseUntilUtc = leaseUntilUtcParam.Value == DBNull.Value ? null : (DateTime?)leaseUntilUtcParam.Value;

        return new LeaseAcquireResult(acquired, serverUtcNow, leaseUntilUtc);
    }

    /// <summary>
    /// Attempts to renew a lease.
    /// </summary>
    /// <param name="name">The lease name.</param>
    /// <param name="owner">The owner identifier.</param>
    /// <param name="leaseSeconds">The lease duration in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the renew operation.</returns>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Uses stored procedure name derived from configured schema.")]
    public async Task<LeaseRenewResult> RenewAsync(
        string name,
        string owner,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = new SqlCommand($"[{schemaName}].[Lease_Renew]", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Owner", owner);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);

        var renewedParam = new SqlParameter("@Renewed", SqlDbType.Bit) { Direction = ParameterDirection.Output };
        var serverUtcNowParam = new SqlParameter("@ServerUtcNow", SqlDbType.DateTime2) { Direction = ParameterDirection.Output };
        var leaseUntilUtcParam = new SqlParameter("@LeaseUntilUtc", SqlDbType.DateTime2) { Direction = ParameterDirection.Output };

        command.Parameters.Add(renewedParam);
        command.Parameters.Add(serverUtcNowParam);
        command.Parameters.Add(leaseUntilUtcParam);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var renewed = (bool)renewedParam.Value;
        var serverUtcNow = (DateTime)serverUtcNowParam.Value;
        var leaseUntilUtc = leaseUntilUtcParam.Value == DBNull.Value ? null : (DateTime?)leaseUntilUtcParam.Value;

        return new LeaseRenewResult(renewed, serverUtcNow, leaseUntilUtc);
    }
}
