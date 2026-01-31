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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

internal static class SqlServerSchemaMigrations
{
    private const int MaxSqlIdentifierLength = 128;
    private const int HashSuffixBytes = 4;

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
            BuildJournalTableName("Outbox", tableName),
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
            BuildJournalTableName("OutboxJoin", outboxTableName),
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
            BuildJournalTableName("Inbox", tableName),
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
            BuildJournalTableName("Scheduler", jobsTableName, jobRunsTableName, timersTableName),
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
            BuildJournalTableName("Fanout", policyTableName, cursorTableName),
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
            BuildJournalTableName("Lease", tableName),
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
            BuildJournalTableName("DistributedLock", tableName),
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
            BuildJournalTableName("Metrics"),
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
            BuildJournalTableName("MetricsCentral"),
            variables,
            logger,
            cancellationToken);
    }

    public static Task ApplyTenantBundleAsync(
        string connectionString,
        string schemaName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var outboxTable = "Outbox";
        var inboxTable = "Inbox";
        var jobsTable = "Jobs";
        var jobRunsTable = "JobRuns";
        var timersTable = "Timers";
        var leaseTable = "Lease";
        var lockTable = "DistributedLock";
        var fanoutPolicyTable = "FanoutPolicy";
        var fanoutCursorTable = "FanoutCursor";
        var externalSideEffectTable = "ExternalSideEffect";
        var idempotencyTable = "Idempotency";

        var scripts = new List<SqlScript>();
        scripts.AddRange(GetModuleScriptsWithVariables("Outbox", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["OutboxTable"] = outboxTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("OutboxJoin", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["OutboxTable"] = outboxTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Inbox", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["InboxTable"] = inboxTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Scheduler", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["JobsTable"] = jobsTable,
            ["JobRunsTable"] = jobRunsTable,
            ["TimersTable"] = timersTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Lease", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["LeaseTable"] = leaseTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("DistributedLock", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["LockTable"] = lockTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Fanout", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["PolicyTable"] = fanoutPolicyTable,
            ["CursorTable"] = fanoutCursorTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("ExternalSideEffects", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["ExternalSideEffectTable"] = externalSideEffectTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Idempotency", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["IdempotencyTable"] = idempotencyTable,
        }));
        scripts.AddRange(GetModuleScriptsWithVariables("Metrics", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = "infra",
        }));

        var journalTable = BuildJournalTableName(
            "TenantBundle",
            outboxTable,
            inboxTable,
            jobsTable,
            jobRunsTable,
            timersTable,
            leaseTable,
            lockTable,
            fanoutPolicyTable,
            fanoutCursorTable,
            externalSideEffectTable,
            idempotencyTable);

        return DbUpSchemaRunner.ApplyAsync(
            connectionString,
            scripts,
            schemaName,
            journalTable,
            new Dictionary<string, string>(StringComparer.Ordinal),
            logger ?? NullLogger.Instance,
            cancellationToken);
    }

    public static Task ApplyControlPlaneBundleAsync(
        string connectionString,
        string schemaName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var scripts = new List<SqlScript>();
        scripts.AddRange(GetModuleScriptsWithVariables("MetricsCentral", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
        }));

        var journalTable = BuildJournalTableName("ControlPlaneBundle", "MetricsCentral");

        return DbUpSchemaRunner.ApplyAsync(
            connectionString,
            scripts,
            schemaName,
            journalTable,
            new Dictionary<string, string>(StringComparer.Ordinal),
            logger ?? NullLogger.Instance,
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
            BuildJournalTableName("ExternalSideEffects", tableName),
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
            BuildJournalTableName("Idempotency", tableName),
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
            BuildJournalTableName("EmailOutbox", tableName),
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

    private static List<SqlScript> GetModuleScripts(string moduleName)
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

    private static List<SqlScript> GetModuleScriptsWithVariables(
        string moduleName,
        IReadOnlyDictionary<string, string> variables)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{assembly.GetName().Name}.SchemaMigrations.{moduleName}.";

        return assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SqlScript(name, ReplaceVariables(ReadResourceText(assembly, name), variables)))
            .ToList();
    }

    private static List<string> GetModuleScriptsText(
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

    private static string BuildJournalTableName(string moduleName, params string[] tableNames)
    {
        var suffixParts = tableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeIdentifier)
            .Where(name => name.Length > 0)
            .ToArray();

        var suffix = suffixParts.Length > 0
            ? "_" + string.Join("_", suffixParts)
            : string.Empty;

        var baseName = suffixParts.Length > 0
            ? "Journal"
            : $"Journal_{NormalizeIdentifier(moduleName)}";

        var candidate = $"{baseName}{suffix}";
        if (candidate.Length <= MaxSqlIdentifierLength)
        {
            return candidate;
        }

        var hashSuffix = ComputeHashSuffix(candidate);
        var maxBaseLength = MaxSqlIdentifierLength - (hashSuffix.Length + 1);
        var truncated = candidate.Length > maxBaseLength
            ? candidate[..maxBaseLength]
            : candidate;

        return $"{truncated}_{hashSuffix}";
    }

    private static string NormalizeIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static string ComputeHashSuffix(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, HashSuffixBytes));
    }
}
