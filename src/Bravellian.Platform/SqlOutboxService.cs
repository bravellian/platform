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

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Threading.Tasks;

internal class SqlOutboxService : IOutbox
{
    private readonly SqlOutboxOptions options;
    private readonly string connectionString;
    private readonly string enqueueSql;

    public SqlOutboxService(IOptions<SqlOutboxOptions> options)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        
        // Build the SQL query using configured schema and table names
        this.enqueueSql = $@"
            INSERT INTO [{this.options.SchemaName}].[{this.options.TableName}] (Topic, Payload, CorrelationId, MessageId)
            VALUES (@Topic, @Payload, @CorrelationId, NEWID());";
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null)
    {
        // Note: We use the connection from the provided transaction.
        await transaction.Connection.ExecuteAsync(this.enqueueSql, new
        {
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
        }, transaction: transaction).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var result = new List<Guid>(batchSize);
        
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand("dbo.Outbox_Claim", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add((Guid)reader.GetValue(0));
        }

        return result;
    }

    public async Task AckAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.Outbox_Ack", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.Outbox_Abandon", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.Outbox_Fail", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand("dbo.Outbox_ReapExpired", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return; // Nothing to do
        }

        var tvp = new DataTable();
        tvp.Columns.Add("Id", typeof(Guid));
        foreach (var id in idList)
        {
            tvp.Rows.Add(id);
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await using var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        var parameter = command.Parameters.AddWithValue("@Ids", tvp);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.GuidIdList";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
