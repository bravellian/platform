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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class PostgresAuditAdapterTests : PostgresTestBase
{
    public PostgresAuditAdapterTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureAuditSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task WriteAndQueryByAnchorRoundTrip()
    {
        var options = Options.Create(new PostgresAuditOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            AuditEventsTable = "AuditEvents",
            AuditAnchorsTable = "AuditAnchors",
        });

        var writer = new PostgresAuditEventWriter(options, NullLogger<PostgresAuditEventWriter>.Instance);
        var reader = new PostgresAuditEventReader(options, NullLogger<PostgresAuditEventReader>.Instance);

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
}

