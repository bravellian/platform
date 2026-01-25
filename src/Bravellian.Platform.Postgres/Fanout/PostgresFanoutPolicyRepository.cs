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
/// PostgreSQL implementation of IFanoutPolicyRepository using Dapper for data access.
/// Manages fanout policy settings in the FanoutPolicy table.
/// </summary>
internal sealed class PostgresFanoutPolicyRepository : IFanoutPolicyRepository
{
    private readonly PostgresFanoutOptions options;
    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresFanoutPolicyRepository"/> class.
    /// </summary>
    /// <param name="options">The fanout configuration options.</param>
    public PostgresFanoutPolicyRepository(IOptions<PostgresFanoutOptions> options)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
    }

    /// <inheritdoc/>
    public async Task<(int everySeconds, int jitterSeconds)> GetCadenceAsync(string fanoutTopic, string workKey, CancellationToken ct)
    {
        const int defaultEverySeconds = 300; // 5 minutes
        const int defaultJitterSeconds = 60; // 1 minute

        // Ensure fanout schema exists before querying
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            connectionString,
            options.SchemaName,
            options.PolicyTableName,
            options.CursorTableName).ConfigureAwait(false);

        var policyTable = PostgresSqlHelper.Qualify(options.SchemaName, options.PolicyTableName);
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $"""
            SELECT "DefaultEverySeconds", "JitterSeconds"
            FROM {policyTable}
            WHERE "FanoutTopic" = @FanoutTopic AND "WorkKey" = @WorkKey;
            """;

        var result = await connection.QueryFirstOrDefaultAsync<(int DefaultEverySeconds, int JitterSeconds)>(
            sql,
            new { FanoutTopic = fanoutTopic, WorkKey = workKey }).ConfigureAwait(false);

        return result == default ? (defaultEverySeconds, defaultJitterSeconds) : result;
    }

    /// <inheritdoc/>
    public async Task SetCadenceAsync(string fanoutTopic, string workKey, int everySeconds, int jitterSeconds, CancellationToken ct)
    {
        var policyTable = PostgresSqlHelper.Qualify(options.SchemaName, options.PolicyTableName);
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = $"""
            INSERT INTO {policyTable}
                ("FanoutTopic", "WorkKey", "DefaultEverySeconds", "JitterSeconds", "UpdatedAt")
            VALUES
                (@FanoutTopic, @WorkKey, @DefaultEverySeconds, @JitterSeconds, CURRENT_TIMESTAMP)
            ON CONFLICT ("FanoutTopic", "WorkKey") DO UPDATE
            SET "DefaultEverySeconds" = EXCLUDED."DefaultEverySeconds",
                "JitterSeconds" = EXCLUDED."JitterSeconds",
                "UpdatedAt" = CURRENT_TIMESTAMP;
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                FanoutTopic = fanoutTopic,
                WorkKey = workKey,
                DefaultEverySeconds = everySeconds,
                JitterSeconds = jitterSeconds,
            }).ConfigureAwait(false);
    }
}





