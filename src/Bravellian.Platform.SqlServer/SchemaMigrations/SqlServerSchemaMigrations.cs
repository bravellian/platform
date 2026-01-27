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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

internal static class SqlServerSchemaMigrations
{
    private const string OutboxJournalTable = "BravellianPlatform_OutboxJournal";
    private const string OutboxJoinJournalTable = "BravellianPlatform_OutboxJoinJournal";
    private const string InboxJournalTable = "BravellianPlatform_InboxJournal";
    private const string SchedulerJournalTable = "BravellianPlatform_SchedulerJournal";
    private const string FanoutJournalTable = "BravellianPlatform_FanoutJournal";
    private const string LeaseJournalTable = "BravellianPlatform_LeaseJournal";
    private const string DistributedLockJournalTable = "BravellianPlatform_DistributedLockJournal";
    private const string SemaphoreJournalTable = "BravellianPlatform_SemaphoreJournal";
    private const string MetricsJournalTable = "BravellianPlatform_MetricsJournal";
    private const string CentralMetricsJournalTable = "BravellianPlatform_CentralMetricsJournal";
    private const string ExternalSideEffectJournalTable = "BravellianPlatform_ExternalSideEffectsJournal";
    private const string IdempotencyJournalTable = "BravellianPlatform_IdempotencyJournal";
    private const string EmailOutboxJournalTable = "BravellianPlatform_EmailOutboxJournal";

    public static Task ApplyOutboxAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["OutboxTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Outbox",
            schemaName,
            OutboxJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyOutboxJoinAsync(
        string connectionString,
        string schemaName,
        string outboxTableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["OutboxTable"] = outboxTableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "OutboxJoin",
            schemaName,
            OutboxJoinJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyInboxAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["InboxTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Inbox",
            schemaName,
            InboxJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplySchedulerAsync(
        string connectionString,
        string schemaName,
        string jobsTableName,
        string jobRunsTableName,
        string timersTableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["JobsTable"] = jobsTableName,
            ["JobRunsTable"] = jobRunsTableName,
            ["TimersTable"] = timersTableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Scheduler",
            schemaName,
            SchedulerJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyFanoutAsync(
        string connectionString,
        string schemaName,
        string policyTableName,
        string cursorTableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["PolicyTable"] = policyTableName,
            ["CursorTable"] = cursorTableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Fanout",
            schemaName,
            FanoutJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyLeaseAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["LeaseTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Lease",
            schemaName,
            LeaseJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyDistributedLockAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["LockTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "DistributedLock",
            schemaName,
            DistributedLockJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplySemaphoreAsync(
        string connectionString,
        string schemaName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Semaphore",
            schemaName,
            SemaphoreJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyMetricsAsync(
        string connectionString,
        string schemaName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Metrics",
            schemaName,
            MetricsJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyCentralMetricsAsync(
        string connectionString,
        string schemaName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
        };

        return ApplyModuleAsync(
            connectionString,
            "MetricsCentral",
            schemaName,
            CentralMetricsJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyExternalSideEffectsAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["ExternalSideEffectTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "ExternalSideEffects",
            schemaName,
            ExternalSideEffectJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyIdempotencyAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["IdempotencyTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "Idempotency",
            schemaName,
            IdempotencyJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyEmailOutboxAsync(
        string connectionString,
        string schemaName,
        string tableName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["EmailOutboxTable"] = tableName,
        };

        return ApplyModuleAsync(
            connectionString,
            "EmailOutbox",
            schemaName,
            EmailOutboxJournalTable,
            variables,
            logger,
            cancellationToken);
    }

    public static IReadOnlyList<string> GetOutboxScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["OutboxTable"] = "Outbox",
        };

        return GetModuleScriptsText("Outbox", variables);
    }

    public static IReadOnlyList<string> GetInboxScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["InboxTable"] = "Inbox",
        };

        return GetModuleScriptsText("Inbox", variables);
    }

    public static IReadOnlyList<string> GetSchedulerScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["JobsTable"] = "Jobs",
            ["JobRunsTable"] = "JobRuns",
            ["TimersTable"] = "Timers",
        };

        return GetModuleScriptsText("Scheduler", variables);
    }

    public static IReadOnlyList<string> GetFanoutScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["PolicyTable"] = "FanoutPolicy",
            ["CursorTable"] = "FanoutCursor",
        };

        return GetModuleScriptsText("Fanout", variables);
    }

    public static IReadOnlyList<string> GetExternalSideEffectScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["ExternalSideEffectTable"] = "ExternalSideEffect",
        };

        return GetModuleScriptsText("ExternalSideEffects", variables);
    }

    public static IReadOnlyList<string> GetIdempotencyScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["IdempotencyTable"] = "Idempotency",
        };

        return GetModuleScriptsText("Idempotency", variables);
    }

    public static IReadOnlyList<string> GetEmailOutboxScriptsForSnapshot()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
            ["EmailOutboxTable"] = "EmailOutbox",
        };

        return GetModuleScriptsText("EmailOutbox", variables);
    }

    private static Task ApplyModuleAsync(
        string connectionString,
        string moduleName,
        string journalSchema,
        string journalTable,
        IReadOnlyDictionary<string, string> variables,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var scripts = GetModuleScripts(moduleName);
        return DbUpSchemaRunner.ApplyAsync(
            connectionString,
            scripts,
            journalSchema,
            journalTable,
            variables,
            logger ?? NullLogger.Instance,
            cancellationToken);
    }

    private static IReadOnlyList<SqlScript> GetModuleScripts(string moduleName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{assembly.GetName().Name}.SchemaMigrations.{moduleName}.";

        return assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SqlScript(name, ReadResourceText(assembly, name)))
            .ToList();
    }

    private static IReadOnlyList<string> GetModuleScriptsText(
        string moduleName,
        IReadOnlyDictionary<string, string> variables)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{assembly.GetName().Name}.SchemaMigrations.{moduleName}.";

        return assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => ReplaceVariables(ReadResourceText(assembly, name), variables))
            .ToList();
    }

    private static string ReadResourceText(Assembly assembly, string name)
    {
        var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource '{name}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ReplaceVariables(string text, IReadOnlyDictionary<string, string> variables)
    {
        var result = text;
        foreach (var pair in variables)
        {
            result = result.Replace($"${pair.Key}$", pair.Value, StringComparison.OrdinalIgnoreCase);
            result = result.Replace($"$({pair.Key})", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
