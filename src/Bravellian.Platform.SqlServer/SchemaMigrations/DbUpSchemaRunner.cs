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

using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Helper to apply SQL Server schema migrations using DBUp.
/// </summary>
internal static class DbUpSchemaRunner
{
    public static async Task ApplyAsync(
        string connectionString,
        IReadOnlyCollection<SqlScript> scripts,
        string journalSchema,
        string journalTable,
        IReadOnlyDictionary<string, string> variables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(logger);

        await EnsureSchemaExistsAsync(connectionString, journalSchema, cancellationToken).ConfigureAwait(false);

        var upgrader = DeployChanges
            .To
            .SqlDatabase(connectionString)
            .WithScripts(scripts)
            .WithVariables(variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
            .JournalToSqlTable(journalSchema, journalTable)
            .LogTo(new DbUpLoggerAdapter(logger))
            .Build();

        var result = await Task.Run(upgrader.PerformUpgrade, cancellationToken).ConfigureAwait(false);
        if (!result.Successful)
        {
            throw result.Error ?? new InvalidOperationException("DBUp schema upgrade failed.");
        }
    }

    private static async Task EnsureSchemaExistsAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @SchemaName)
            BEGIN
                EXEC('CREATE SCHEMA [' + @SchemaName + ']');
            END
            """;

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class DbUpLoggerAdapter : IUpgradeLog
    {
        private readonly ILogger logger;

        public DbUpLoggerAdapter(ILogger logger)
        {
            this.logger = logger;
        }

        public void LogTrace(string format, params object[] args)
        {
            logger.LogTrace(format, args);
        }

        public void LogDebug(string format, params object[] args)
        {
            logger.LogDebug(format, args);
        }

        public void LogInformation(string format, params object[] args)
        {
            logger.LogInformation(format, args);
        }

        public void LogWarning(string format, params object[] args)
        {
            logger.LogWarning(format, args);
        }

        public void LogError(string format, params object[] args)
        {
            logger.LogError(format, args);
        }

        public void LogError(Exception ex, string format, params object[] args)
        {
            logger.LogError(ex, format, args);
        }
    }
}

