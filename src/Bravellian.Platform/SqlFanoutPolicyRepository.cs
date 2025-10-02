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
/// SQL Server implementation of IFanoutPolicyRepository using Dapper for data access.
/// Manages fanout policy settings in the FanoutPolicy table.
/// </summary>
internal sealed class SqlFanoutPolicyRepository : IFanoutPolicyRepository
{
    private readonly SqlFanoutOptions options;
    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlFanoutPolicyRepository"/> class.
    /// </summary>
    /// <param name="options">The fanout configuration options.</param>
    public SqlFanoutPolicyRepository(IOptions<SqlFanoutOptions> options)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
    }

    /// <inheritdoc/>
    public async Task<(int everySeconds, int jitterSeconds)> GetCadenceAsync(string fanoutTopic, string workKey, CancellationToken ct)
    {
        const int defaultEverySeconds = 300; // 5 minutes
        const int defaultJitterSeconds = 60; // 1 minute

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $@"
            SELECT DefaultEverySeconds, JitterSeconds 
            FROM [{this.options.SchemaName}].[{this.options.PolicyTableName}]
            WHERE FanoutTopic = @FanoutTopic AND WorkKey = @WorkKey";

        var result = await connection.QueryFirstOrDefaultAsync<(int DefaultEverySeconds, int JitterSeconds)>(
            sql, 
            new { FanoutTopic = fanoutTopic, WorkKey = workKey }).ConfigureAwait(false);

        return result == default ? (defaultEverySeconds, defaultJitterSeconds) : result;
    }

    /// <inheritdoc/>
    public async Task SetCadenceAsync(string fanoutTopic, string workKey, int everySeconds, int jitterSeconds, CancellationToken ct)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $@"
            MERGE [{this.options.SchemaName}].[{this.options.PolicyTableName}] AS target
            USING (VALUES (@FanoutTopic, @WorkKey, @DefaultEverySeconds, @JitterSeconds)) AS source (FanoutTopic, WorkKey, DefaultEverySeconds, JitterSeconds)
            ON target.FanoutTopic = source.FanoutTopic AND target.WorkKey = source.WorkKey
            WHEN MATCHED THEN
                UPDATE SET DefaultEverySeconds = source.DefaultEverySeconds, JitterSeconds = source.JitterSeconds
            WHEN NOT MATCHED THEN
                INSERT (FanoutTopic, WorkKey, DefaultEverySeconds, JitterSeconds)
                VALUES (source.FanoutTopic, source.WorkKey, source.DefaultEverySeconds, source.JitterSeconds);";

        await connection.ExecuteAsync(
            sql,
            new 
            { 
                FanoutTopic = fanoutTopic, 
                WorkKey = workKey, 
                DefaultEverySeconds = everySeconds, 
                JitterSeconds = jitterSeconds 
            }).ConfigureAwait(false);
    }
}