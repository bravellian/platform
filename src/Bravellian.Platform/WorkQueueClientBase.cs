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

namespace Bravellian.Platform;

using Microsoft.Data.SqlClient;
using System.Data;

/// <summary>
/// Base implementation of a work queue client that provides claim-and-process semantics.
/// </summary>
/// <typeparam name="TId">The type of the queue item identifier.</typeparam>
public abstract class WorkQueueClientBase<TId> : IWorkQueueClient<TId>
{
    private readonly string connectionString;
    private readonly string claimProcedure;
    private readonly string ackProcedure;
    private readonly string abandonProcedure;
    private readonly string failProcedure;
    private readonly string reapExpiredProcedure;
    private readonly string tableValuedParameterTypeName;

    protected WorkQueueClientBase(WorkQueueOptions options)
    {
        this.connectionString = options.ConnectionString;
        
        var prefix = options.ProcedureNamePrefix;
        this.claimProcedure = $"{prefix}_Claim";
        this.ackProcedure = $"{prefix}_Ack";
        this.abandonProcedure = $"{prefix}_Abandon";
        this.failProcedure = $"{prefix}_Fail";
        this.reapExpiredProcedure = $"{prefix}_ReapExpired";
        
        this.tableValuedParameterTypeName = this.GetTableValuedParameterTypeName();
    }

    public virtual async Task<IReadOnlyList<TId>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var result = new List<TId>(batchSize);
        
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand(this.claimProcedure, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(this.ConvertFromDatabase(reader.GetValue(0)));
        }

        return result;
    }

    public virtual async Task AckAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync(this.ackProcedure, ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync(this.abandonProcedure, ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task FailAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync(this.failProcedure, ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task ReapExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand(this.reapExpiredProcedure, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a database value to the typed identifier.
    /// </summary>
    /// <param name="dbValue">The value from the database.</param>
    /// <returns>The typed identifier.</returns>
    protected abstract TId ConvertFromDatabase(object dbValue);

    /// <summary>
    /// Gets the name of the table-valued parameter type for IDs.
    /// </summary>
    /// <returns>The table-valued parameter type name.</returns>
    protected abstract string GetTableValuedParameterTypeName();

    /// <summary>
    /// Creates a DataTable with the IDs for use with table-valued parameters.
    /// </summary>
    /// <param name="ids">The identifiers to include.</param>
    /// <returns>A DataTable containing the IDs.</returns>
    protected abstract DataTable CreateIdDataTable(IEnumerable<TId> ids);

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return; // Nothing to do
        }

        var tvp = this.CreateIdDataTable(idList);

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        var parameter = command.Parameters.AddWithValue("@Ids", tvp);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = this.tableValuedParameterTypeName;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}