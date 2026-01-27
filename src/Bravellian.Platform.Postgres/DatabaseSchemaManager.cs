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

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

/// <summary>
/// Manages database schema creation and verification for the Postgres Platform components.
/// </summary>
internal static class DatabaseSchemaManager
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static Task EnsureOutboxSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string tableName = "Outbox")
    {
        return PostgresSchemaMigrations.ApplyOutboxAsync(
            connectionString,
            schemaName,
            tableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureOutboxJoinSchemaAsync(
        string connectionString,
        string schemaName = "infra")
    {
        return PostgresSchemaMigrations.ApplyOutboxJoinAsync(
            connectionString,
            schemaName,
            "Outbox",
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureDistributedLockSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string tableName = "DistributedLock")
    {
        return PostgresSchemaMigrations.ApplyDistributedLockAsync(
            connectionString,
            schemaName,
            tableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureLeaseSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string tableName = "Lease")
    {
        return PostgresSchemaMigrations.ApplyLeaseAsync(
            connectionString,
            schemaName,
            tableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureInboxSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string tableName = "Inbox")
    {
        return PostgresSchemaMigrations.ApplyInboxAsync(
            connectionString,
            schemaName,
            tableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureSchedulerSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string jobsTableName = "Jobs",
        string jobRunsTableName = "JobRuns",
        string timersTableName = "Timers")
    {
        return PostgresSchemaMigrations.ApplySchedulerAsync(
            connectionString,
            schemaName,
            jobsTableName,
            jobRunsTableName,
            timersTableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureFanoutSchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string policyTableName = "FanoutPolicy",
        string cursorTableName = "FanoutCursor")
    {
        return PostgresSchemaMigrations.ApplyFanoutAsync(
            connectionString,
            schemaName,
            policyTableName,
            cursorTableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureIdempotencySchemaAsync(
        string connectionString,
        string schemaName = "infra",
        string tableName = "Idempotency")
    {
        return PostgresSchemaMigrations.ApplyIdempotencyAsync(
            connectionString,
            schemaName,
            tableName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureSemaphoreSchemaAsync(
        string connectionString,
        string schemaName = "infra")
    {
        return PostgresSchemaMigrations.ApplySemaphoreAsync(
            connectionString,
            schemaName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureMetricsSchemaAsync(
        string connectionString,
        string schemaName = "infra")
    {
        return PostgresSchemaMigrations.ApplyMetricsAsync(
            connectionString,
            schemaName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    public static Task EnsureCentralMetricsSchemaAsync(
        string connectionString,
        string schemaName = "infra")
    {
        return PostgresSchemaMigrations.ApplyCentralMetricsAsync(
            connectionString,
            schemaName,
            NullLogger.Instance,
            CancellationToken.None);
    }

    /// <summary>
    /// Ensures the work queue schema exists. This is now a wrapper around outbox schema deployment.
    /// </summary>
    public static Task EnsureWorkQueueSchemaAsync(string connectionString, string schemaName = "infra")
    {
        return EnsureOutboxSchemaAsync(connectionString, schemaName, "Outbox");
    }

    /// <summary>
    /// Ensures the inbox work queue schema exists. This is now a wrapper around inbox schema deployment.
    /// </summary>
    public static Task EnsureInboxWorkQueueSchemaAsync(string connectionString, string schemaName = "infra")
    {
        return EnsureInboxSchemaAsync(connectionString, schemaName, "Inbox");
    }

    internal static IReadOnlyDictionary<string, string> GetSchemaVersionsForSnapshot()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["outbox"] = ComputeSchemaHash(PostgresSchemaMigrations.GetOutboxScriptsForSnapshot()),
            ["inbox"] = ComputeSchemaHash(PostgresSchemaMigrations.GetInboxScriptsForSnapshot()),
            ["scheduler"] = ComputeSchemaHash(PostgresSchemaMigrations.GetSchedulerScriptsForSnapshot()),
            ["fanout"] = ComputeSchemaHash(PostgresSchemaMigrations.GetFanoutScriptsForSnapshot()),
            ["idempotency"] = ComputeSchemaHash(PostgresSchemaMigrations.GetIdempotencyScriptsForSnapshot()),
        };
    }

    private static string ComputeSchemaHash(IEnumerable<string> scripts)
    {
        var builder = new StringBuilder();

        foreach (var script in scripts)
        {
            builder.AppendLine(script);
        }

        var normalized = NormalizeScriptsForHash(builder.ToString());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeScriptsForHash(string scriptsText)
    {
        var normalizedLineEndings = scriptsText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        var resultBuilder = new StringBuilder();
        var lines = normalizedLineEndings.Split('\n');

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }

            var normalizedLine = WhitespaceRegex.Replace(trimmedLine, " ").Trim();
            if (normalizedLine.Length == 0)
            {
                continue;
            }

            if (resultBuilder.Length > 0)
            {
                resultBuilder.Append('\n');
            }

            resultBuilder.Append(normalizedLine);
        }

        return resultBuilder.ToString();
    }
}



