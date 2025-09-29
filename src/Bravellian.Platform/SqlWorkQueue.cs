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

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// SQL Server implementation of work queue operations.
/// </summary>
/// <typeparam name="T">The type of item identifiers.</typeparam>
public abstract class SqlWorkQueueBase<T> : IWorkQueue<T>
{
    private readonly string connectionString;
    protected readonly string schemaName;
    protected readonly string tableName;
    private readonly string tvpTypeName;

    protected SqlWorkQueueBase(
        string connectionString,
        string schemaName,
        string tableName,
        string tvpTypeName)
    {
        this.connectionString = connectionString;
        this.schemaName = schemaName;
        this.tableName = tableName;
        this.tvpTypeName = tvpTypeName;
    }

    public async Task<IReadOnlyList<T>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var procedureName = GetClaimProcedureName();
        var result = new List<T>();

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(procedureName, connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ConvertFromDb(reader.GetValue(0)));
        }

        return result;
    }

    public async Task AckAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithIdsAsync($"[{this.schemaName}].[{this.tableName}_Ack]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithIdsAsync($"[{this.schemaName}].[{this.tableName}_Abandon]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithIdsAsync($"[{this.schemaName}].[{this.tableName}_Fail]", ownerToken, ids, cancellationToken, errorMessage).ConfigureAwait(false);
    }

    public async Task<int> ReapExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand($"[{this.schemaName}].[{this.tableName}_ReapExpired]", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    protected abstract string GetClaimProcedureName();
    protected abstract T ConvertFromDb(object dbValue);
    protected abstract object ConvertToDb(T value);

    private async Task ExecuteWithIdsAsync(
        string procedureName,
        Guid ownerToken,
        IEnumerable<T> ids,
        CancellationToken cancellationToken,
        string? errorMessage = null)
    {
        var idList = ids.ToList();
        if (!idList.Any()) return;

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(T));
        foreach (var id in idList)
        {
            dataTable.Rows.Add(ConvertToDb(id));
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(procedureName, connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@OwnerToken", ownerToken);

        var idsParameter = command.Parameters.AddWithValue("@Ids", dataTable);
        idsParameter.SqlDbType = SqlDbType.Structured;
        idsParameter.TypeName = $"dbo.{this.tvpTypeName}";

        if (errorMessage != null)
        {
            command.Parameters.AddWithValue("@ErrorMessage", errorMessage);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Work queue implementation for UNIQUEIDENTIFIER primary keys.
/// </summary>
public class SqlGuidWorkQueue : SqlWorkQueueBase<Guid>
{
    private readonly bool isScheduled;

    public SqlGuidWorkQueue(
        string connectionString,
        string schemaName,
        string tableName,
        bool isScheduled = false)
        : base(connectionString, schemaName, tableName, "UniqueIdentifierIdList")
    {
        this.isScheduled = isScheduled;
    }

    protected override string GetClaimProcedureName()
    {
        var suffix = this.isScheduled ? "_ClaimDue" : "_Claim";
        return $"[{base.schemaName}].[{base.tableName}{suffix}]";
    }

    protected override Guid ConvertFromDb(object dbValue) => (Guid)dbValue;
    protected override object ConvertToDb(Guid value) => value;
}

/// <summary>
/// Work queue implementation for BIGINT primary keys.
/// </summary>
public class SqlBigIntWorkQueue : SqlWorkQueueBase<long>
{
    private readonly bool isScheduled;

    public SqlBigIntWorkQueue(
        string connectionString,
        string schemaName,
        string tableName,
        bool isScheduled = false)
        : base(connectionString, schemaName, tableName, "BigIntIdList")
    {
        this.isScheduled = isScheduled;
    }

    protected override string GetClaimProcedureName()
    {
        var suffix = this.isScheduled ? "_ClaimDue" : "_Claim";
        return $"[{base.schemaName}].[{base.tableName}{suffix}]";
    }

    protected override long ConvertFromDb(object dbValue) => Convert.ToInt64(dbValue);
    protected override object ConvertToDb(long value) => value;
}