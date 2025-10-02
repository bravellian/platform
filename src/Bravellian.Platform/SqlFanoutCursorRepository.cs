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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server implementation of IFanoutCursorRepository using Dapper for data access.
/// Manages completion cursors in the FanoutCursor table to track processing progress.
/// </summary>
internal sealed class SqlFanoutCursorRepository : IFanoutCursorRepository
{
    private readonly SqlFanoutOptions options;
    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlFanoutCursorRepository"/> class.
    /// </summary>
    /// <param name="options">The fanout configuration options.</param>
    public SqlFanoutCursorRepository(IOptions<SqlFanoutOptions> options)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLastAsync(string fanoutTopic, string workKey, string shardKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $@"
            SELECT LastCompletedAt 
            FROM [{this.options.SchemaName}].[{this.options.CursorTableName}]
            WHERE FanoutTopic = @FanoutTopic AND WorkKey = @WorkKey AND ShardKey = @ShardKey";

        var result = await connection.QueryFirstOrDefaultAsync<DateTimeOffset?>(
            sql,
            new { FanoutTopic = fanoutTopic, WorkKey = workKey, ShardKey = shardKey }).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc/>
    public async Task MarkCompletedAsync(string fanoutTopic, string workKey, string shardKey, DateTimeOffset completedAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $@"
            MERGE [{this.options.SchemaName}].[{this.options.CursorTableName}] AS target
            USING (VALUES (@FanoutTopic, @WorkKey, @ShardKey, @LastCompletedAt)) AS source (FanoutTopic, WorkKey, ShardKey, LastCompletedAt)
            ON target.FanoutTopic = source.FanoutTopic AND target.WorkKey = source.WorkKey AND target.ShardKey = source.ShardKey
            WHEN MATCHED THEN
                UPDATE SET LastCompletedAt = source.LastCompletedAt
            WHEN NOT MATCHED THEN
                INSERT (FanoutTopic, WorkKey, ShardKey, LastCompletedAt)
                VALUES (source.FanoutTopic, source.WorkKey, source.ShardKey, source.LastCompletedAt);";

        await connection.ExecuteAsync(
            sql,
            new 
            { 
                FanoutTopic = fanoutTopic, 
                WorkKey = workKey, 
                ShardKey = shardKey, 
                LastCompletedAt = completedAt 
            }).ConfigureAwait(false);
    }
}