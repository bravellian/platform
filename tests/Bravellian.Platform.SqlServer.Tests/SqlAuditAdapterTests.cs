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

using Bravellian.Platform.Audit;
using Bravellian.Platform.Correlation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SqlAuditAdapterTests : SqlServerTestBase
{
    public SqlAuditAdapterTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture sharedFixture)
        : base(testOutputHelper, sharedFixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ApplyScriptsAsync(SqlAuditSchemaScripts.GetScripts("infra", "AuditEvents", "AuditAnchors"))
            .ConfigureAwait(false);
    }

    /// <summary>When write And Query By Anchor Round Trip, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for write And Query By Anchor Round Trip.</intent>
    /// <scenario>Given write And Query By Anchor Round Trip.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task WriteAndQueryByAnchorRoundTrip()
    {
        var options = Options.Create(new SqlAuditOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            AuditEventsTable = "AuditEvents",
            AuditAnchorsTable = "AuditAnchors",
        });

        var writer = new SqlAuditEventWriter(options, NullLogger<SqlAuditEventWriter>.Instance);
        var reader = new SqlAuditEventReader(options, NullLogger<SqlAuditEventReader>.Instance);

        var occurredAt = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var correlation = new CorrelationContext(
            new CorrelationId("corr-audit"),
            null,
            "trace-audit",
            "span-audit",
            occurredAt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tenant"] = "t-42" });

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            occurredAt,
            "invoice.created",
            "Invoice created",
            EventOutcome.Success,
            new[] { new EventAnchor("Invoice", "INV-1", "Subject") },
            "{\"id\":\"INV-1\"}",
            new AuditActor("System", "svc", "System"),
            correlation);

        await writer.WriteAsync(auditEvent, CancellationToken.None);

        var query = new AuditQuery(
            new[] { new EventAnchor("Invoice", "INV-1", "Subject") },
            fromUtc: occurredAt.AddMinutes(-1),
            toUtc: occurredAt.AddMinutes(1),
            name: "invoice.created",
            limit: 10);

        var results = await reader.QueryAsync(query, CancellationToken.None);

        results.Count.ShouldBe(1);
        var readEvent = results[0];
        readEvent.DisplayMessage.ShouldBe("Invoice created");
        readEvent.Anchors.Count.ShouldBe(1);
        readEvent.Anchors[0].AnchorType.ShouldBe("Invoice");
        readEvent.Correlation.ShouldNotBeNull();
        readEvent.Correlation!.Tags.ShouldNotBeNull();
        readEvent.Correlation.Tags!["tenant"].ShouldBe("t-42");
    }

    private async Task ApplyScriptsAsync(IEnumerable<string> scripts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (var script in scripts)
        {
            await ExecuteSqlScriptAsync(connection, script).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteSqlScriptAsync(SqlConnection connection, string script)
    {
        var batches = script.Split(
            new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO", "GO\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            await using var command = new SqlCommand(trimmed, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }
}

