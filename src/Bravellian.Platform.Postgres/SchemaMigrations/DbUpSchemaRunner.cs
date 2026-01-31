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
using System.Globalization;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Helper to apply PostgreSQL schema migrations using DBUp.
/// </summary>
internal static class DbUpSchemaRunner
{
    private const string SchemaLockScope = "Bravellian.Platform.Postgres.SchemaDeployment";

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

        var lockKey = CreateSchemaLockKey(connectionString, journalSchema);
        var lockConnection = new NpgsqlConnection(connectionString);
        await using (lockConnection.ConfigureAwait(false))
        {
            await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await AcquireAdvisoryLockAsync(lockConnection, lockKey, cancellationToken).ConfigureAwait(false);

            try
            {
                await EnsureSchemaExistsAsync(connectionString, journalSchema, cancellationToken).ConfigureAwait(false);

                var upgrader = DeployChanges
                    .To
                    .PostgresqlDatabase(connectionString)
                    .WithScripts(scripts)
                    .WithVariables(variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
                    .JournalToPostgresqlTable(journalSchema, journalTable)
                    .LogTo(new DbUpLoggerAdapter(logger))
                    .Build();

                var result = await Task.Run(upgrader.PerformUpgrade, cancellationToken).ConfigureAwait(false);
                if (!result.Successful)
                {
                    throw result.Error ?? new InvalidOperationException("DBUp schema upgrade failed.");
                }
            }
            finally
            {
                await ReleaseAdvisoryLockAsync(lockConnection, lockKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Schema name is validated and quoted before execution.")]
    private static async Task EnsureSchemaExistsAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        var sql = $"CREATE SCHEMA IF NOT EXISTS {PostgresSqlHelper.QuoteIdentifier(schemaName)};";

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.OrdinalIgnoreCase) &&
                                          string.Equals(ex.ConstraintName, "pg_namespace_nspname_index", StringComparison.Ordinal))
        {
            // Concurrent CREATE SCHEMA IF NOT EXISTS can still raise a unique violation; ignore.
        }
    }

    private static long CreateSchemaLockKey(string connectionString, string journalSchema)
    {
        var input = string.Create(
            CultureInfo.InvariantCulture,
            $"{SchemaLockScope}|{connectionString}|{journalSchema}");

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(hash.AsSpan(0, sizeof(long)));
    }

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        long lockKey,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand("SELECT pg_advisory_lock(@lockKey);", connection);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("lockKey", lockKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReleaseAdvisoryLockAsync(
        NpgsqlConnection connection,
        long lockKey,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockKey);", connection);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("lockKey", lockKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
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
            logger.LogTrace("{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public void LogDebug(string format, params object[] args)
        {
            logger.LogDebug("{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public void LogInformation(string format, params object[] args)
        {
            logger.LogInformation("{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public void LogWarning(string format, params object[] args)
        {
            logger.LogWarning("{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public void LogError(string format, params object[] args)
        {
            logger.LogError("{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public void LogError(Exception ex, string format, params object[] args)
        {
            logger.LogError(ex, "{Message}", string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}






