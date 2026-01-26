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

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
public class SqlScriptDeploymentTests
{
    private readonly SqlServerCollectionFixture fixture;

    public SqlScriptDeploymentTests(SqlServerCollectionFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>When SQL artifacts are applied in order, then all scripts execute without errors.</summary>
    /// <intent>Verify the ordered SQL script set can be deployed sequentially.</intent>
    /// <scenario>Given a new test database and the ordered SQL script list from the database folder.</scenario>
    /// <behavior>Then each script batch executes successfully in sequence.</behavior>
    [Fact]
    public async Task SqlArtifacts_DeployInOrder()
    {
        var databaseFolder = Path.GetDirectoryName(SchemaVersionSnapshot.SnapshotFilePath);
        Assert.False(string.IsNullOrEmpty(databaseFolder), "Could not locate database folder via schema snapshot path.");

        var connectionString = await fixture.CreateTestDatabaseAsync("sql_artifacts");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var orderedScripts = new[]
        {
            "Outbox.sql",
            "OutboxWorkQueueProcs.sql",
            "Inbox.sql",
            "Timers.sql",
            "TimersWorkQueueProcs.sql",
            "Jobs.sql",
            "JobRuns.sql",
            "JobRunsWorkQueueProcs.sql",
        };

        foreach (var scriptFile in orderedScripts)
        {
            var fullPath = Path.Combine(databaseFolder!, scriptFile);
            Assert.True(File.Exists(fullPath), $"Expected SQL artifact '{scriptFile}' to exist.");

            var scriptText = await File.ReadAllTextAsync(fullPath, TestContext.Current.CancellationToken);
            foreach (var batch in SplitSqlBatches(scriptText))
            {
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static IEnumerable<string> SplitSqlBatches(string scriptText)
    {
        return Regex
            .Split(scriptText, "^\\s*GO\\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0);
    }
}
