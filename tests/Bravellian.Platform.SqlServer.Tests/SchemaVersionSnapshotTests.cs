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

using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests;

public class SchemaVersionSnapshotTests
{
    /// <summary>When the schema snapshot is captured, then it matches the stored snapshot file.</summary>
    /// <intent>Verify schema version snapshots remain consistent across runs.</intent>
    /// <scenario>Given a captured schema snapshot and the existing snapshot file (or a refreshed snapshot).</scenario>
    /// <behavior>Then the captured snapshot equals the stored snapshot content.</behavior>
    [Fact]
    public async Task SchemaVersions_MatchSnapshot()
    {
        var captured = await SchemaVersionSnapshot.CaptureAsync();
        var existingSnapshot = await SchemaVersionSnapshot.TryLoadSnapshotAsync(Xunit.TestContext.Current.CancellationToken);

        if (SchemaVersionSnapshot.ShouldRefreshFromEnvironment())
        {
            await SchemaVersionSnapshot.WriteSnapshotAsync(captured, Xunit.TestContext.Current.CancellationToken);
            existingSnapshot = captured;
        }

        Assert.True(
            existingSnapshot != null,
            $"Schema snapshot is missing at {SchemaVersionSnapshot.SnapshotFilePath}. " +
            "Run with UPDATE_SCHEMA_SNAPSHOT=1 to generate a fresh snapshot.");

        captured.ShouldBe(existingSnapshot);
    }
}

