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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bravellian.Platform.Tests;

internal static class SchemaVersionSnapshot
{
    private const string UpdateSnapshotEnvironmentVariable = "UPDATE_SCHEMA_SNAPSHOT";
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public static string SnapshotFilePath => GetSnapshotFilePath();

    private static string GetSnapshotFilePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var currentDirectory = new DirectoryInfo(baseDirectory);

        while (currentDirectory != null)
        {
            var candidate = Path.Combine(
                currentDirectory.FullName,
                "src",
                "Bravellian.Platform.Database",
                "schema-versions.json");

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            currentDirectory = currentDirectory.Parent;
        }

        // Fallback to the original relative traversal in case discovery fails,
        // to avoid changing behavior in existing environments.
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Bravellian.Platform.Database",
            "schema-versions.json"));
    }
    public static bool ShouldRefreshFromEnvironment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(UpdateSnapshotEnvironmentVariable),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    public static Task<IDictionary<string, string>> CaptureAsync()
    {
        var snapshot = new Dictionary<string, string>(DatabaseSchemaManager.GetSchemaVersionsForSnapshot(), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IDictionary<string, string>>(snapshot);
    }

    public static async Task<IDictionary<string, string>?> TryLoadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SnapshotFilePath))
        {
            return null;
        }

        var stream = File.OpenRead(SnapshotFilePath);
        await using (stream.ConfigureAwait(false))
        {
            Dictionary<string, string>? snapshot;
        try
        {
            snapshot = await JsonSerializer
                .DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize schema version snapshot from '{SnapshotFilePath}'. " +
                "The file exists but contains invalid JSON.",
                ex);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Schema version snapshot file '{SnapshotFilePath}' is empty or does not contain a valid JSON object.");
        }
        return snapshot;
        }
    }

    public static async Task WriteSnapshotAsync(
        IDictionary<string, string> snapshot,
        CancellationToken cancellationToken)
    {
        var stream = File.Open(SnapshotFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, jsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
