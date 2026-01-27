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

using System.Reflection;

namespace Bravellian.Platform;

/// <summary>
/// Provides SQL scripts for SQL Server operation tracking tables.
/// </summary>
public static class SqlOperationSchemaScripts
{
    /// <summary>
    /// Returns the embedded schema scripts with variables applied.
    /// </summary>
    /// <param name="schemaName">Schema name.</param>
    /// <param name="operationsTable">Operations table name.</param>
    /// <param name="operationEventsTable">Operation events table name.</param>
    /// <returns>Ordered list of scripts.</returns>
    public static IReadOnlyList<string> GetScripts(
        string schemaName,
        string operationsTable,
        string operationEventsTable)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaName"] = schemaName,
            ["OperationsTable"] = operationsTable,
            ["OperationEventsTable"] = operationEventsTable,
        };

        return GetScripts(variables);
    }

    private static IReadOnlyList<string> GetScripts(IReadOnlyDictionary<string, string> variables)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{assembly.GetName().Name}.sql.";

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

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReplaceVariables(string text, IReadOnlyDictionary<string, string> variables)
    {
        var result = text;
        foreach (var pair in variables)
        {
            result = result.Replace($"$({pair.Key})", pair.Value, StringComparison.OrdinalIgnoreCase);
            result = result.Replace($"${pair.Key}$", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}