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

/// <summary>
/// SQL Server implementation of IOutboxStore using optimized queries with proper locking hints.
/// </summary>
internal class SqlOutboxStore : IOutboxStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly TimeProvider timeProvider;

    public SqlOutboxStore(IOptions<SqlOutboxOptions> options, TimeProvider timeProvider)
    {
        var opts = options.Value;
        this.connectionString = opts.ConnectionString;
        this.schemaName = opts.SchemaName;
        this.tableName = opts.TableName;
        this.timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        // Use READPAST to skip locked rows, UPDLOCK to prevent other readers from claiming same rows
        var sql = $@"
            SELECT TOP ({limit}) * 
            FROM [{this.schemaName}].[{this.tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE IsProcessed = 0 
              AND NextAttemptAt <= SYSDATETIMEOFFSET()
            ORDER BY CreatedAt";

        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        var messages = await connection.QueryAsync<OutboxMessage>(sql).ConfigureAwait(false);
        return messages.ToList();
    }

    public async Task MarkDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $@"
            UPDATE [{this.schemaName}].[{this.tableName}]
            SET IsProcessed = 1, 
                ProcessedAt = SYSDATETIMEOFFSET(),
                ProcessedBy = @ProcessedBy
            WHERE Id = @Id";

        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await connection.ExecuteAsync(sql, new
        {
            Id = id,
            ProcessedBy = Environment.MachineName
        }).ConfigureAwait(false);
    }

    public async Task RescheduleAsync(Guid id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
    {
        var nextAttempt = this.timeProvider.GetUtcNow().Add(delay);
        
        var sql = $@"
            UPDATE [{this.schemaName}].[{this.tableName}]
            SET RetryCount = RetryCount + 1,
                LastError = @LastError,
                NextAttemptAt = @NextAttemptAt
            WHERE Id = @Id";

        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await connection.ExecuteAsync(sql, new
        {
            Id = id,
            LastError = lastError,
            NextAttemptAt = nextAttempt
        }).ConfigureAwait(false);
    }

    public async Task FailAsync(Guid id, string lastError, CancellationToken cancellationToken)
    {
        var sql = $@"
            UPDATE [{this.schemaName}].[{this.tableName}]
            SET IsProcessed = 1,
                ProcessedAt = SYSDATETIMEOFFSET(),
                ProcessedBy = @ProcessedBy,
                LastError = @LastError
            WHERE Id = @Id";

        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        await connection.ExecuteAsync(sql, new
        {
            Id = id,
            ProcessedBy = $"{Environment.MachineName}:FAILED",
            LastError = lastError
        }).ConfigureAwait(false);
    }
}