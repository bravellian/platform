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

    public static string SnapshotFilePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "src",
        "Bravellian.Platform.Database",
        "schema-versions.json"));

    public static bool ShouldRefreshFromEnvironment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(UpdateSnapshotEnvironmentVariable),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    public static Task<IDictionary<string, string>> CaptureAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        _ = connectionString;
        _ = cancellationToken;

        var snapshot = new Dictionary<string, string>(DatabaseSchemaManager.GetSchemaVersionsForSnapshot(), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IDictionary<string, string>>(snapshot);
    }

    public static async Task<IDictionary<string, string>?> TryLoadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SnapshotFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(SnapshotFilePath);
        var snapshot = await JsonSerializer
            .DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return snapshot;
    }

    public static async Task WriteSnapshotAsync(
        IDictionary<string, string> snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(SnapshotFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Open(SnapshotFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
