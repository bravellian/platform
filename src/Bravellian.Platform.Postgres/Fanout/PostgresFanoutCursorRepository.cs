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
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of IFanoutCursorRepository using Dapper for data access.
/// Manages completion cursors in the FanoutCursor table to track processing progress.
/// </summary>
internal sealed class PostgresFanoutCursorRepository : IFanoutCursorRepository
{
    private readonly PostgresFanoutOptions options;
    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresFanoutCursorRepository"/> class.
    /// </summary>
    /// <param name="options">The fanout configuration options.</param>
    public PostgresFanoutCursorRepository(IOptions<PostgresFanoutOptions> options)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLastAsync(string fanoutTopic, string workKey, string shardKey, CancellationToken ct)
    {
        // Ensure fanout schema exists before querying
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            connectionString,
            options.SchemaName,
            options.PolicyTableName,
            options.CursorTableName).ConfigureAwait(false);

        var cursorTable = PostgresSqlHelper.Qualify(options.SchemaName, options.CursorTableName);
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $"""
            SELECT "LastCompletedAt"
            FROM {cursorTable}
            WHERE "FanoutTopic" = @FanoutTopic
                AND "WorkKey" = @WorkKey
                AND "ShardKey" = @ShardKey;
            """;

        var result = await connection.QueryFirstOrDefaultAsync<DateTimeOffset?>(
            sql,
            new { FanoutTopic = fanoutTopic, WorkKey = workKey, ShardKey = shardKey }).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc/>
    public async Task MarkCompletedAsync(string fanoutTopic, string workKey, string shardKey, DateTimeOffset completedAt, CancellationToken ct)
    {
        var cursorTable = PostgresSqlHelper.Qualify(options.SchemaName, options.CursorTableName);
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $"""
            INSERT INTO {cursorTable}
                ("FanoutTopic", "WorkKey", "ShardKey", "LastCompletedAt", "UpdatedAt")
            VALUES
                (@FanoutTopic, @WorkKey, @ShardKey, @LastCompletedAt, CURRENT_TIMESTAMP)
            ON CONFLICT ("FanoutTopic", "WorkKey", "ShardKey") DO UPDATE
            SET "LastCompletedAt" = EXCLUDED."LastCompletedAt",
                "UpdatedAt" = CURRENT_TIMESTAMP;
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                FanoutTopic = fanoutTopic,
                WorkKey = workKey,
                ShardKey = shardKey,
                LastCompletedAt = completedAt,
            }).ConfigureAwait(false);
    }
}





