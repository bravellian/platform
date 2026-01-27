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

using Bravellian.Platform.Correlation;
using Bravellian.Platform.Email;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class PostgresEmailDeliverySinkTests : PostgresTestBase
{
    private PostgresEmailDeliverySink? sink;
    private PostgresEmailDeliveryOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "EmailDeliveryEvents",
    };
    private FakeTimeProvider timeProvider = default!;
    private string qualifiedTable = string.Empty;

    public PostgresEmailDeliverySinkTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureEmailDeliverySchemaAsync(ConnectionString).ConfigureAwait(false);

        timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        options.ConnectionString = ConnectionString;
        qualifiedTable = PostgresSqlHelper.Qualify(options.SchemaName, options.TableName);
        sink = new PostgresEmailDeliverySink(
            Options.Create(options),
            timeProvider,
            correlationAccessor: null,
            NullLogger<PostgresEmailDeliverySink>.Instance);
    }

    /// <summary>When a message is queued, then a delivery event is recorded.</summary>
    /// <intent>Capture queued delivery events.</intent>
    /// <scenario>Given a new message.</scenario>
    /// <behavior>Stores a queued event with payload and timestamp.</behavior>
    [Fact]
    public async Task RecordQueued_WritesQueuedEvent()
    {
        var message = CreateMessage("queued-1");

        await sink!.RecordQueuedAsync(message, TestContext.Current.CancellationToken);

        var row = await GetByMessageKeyAsync("queued-1");
        row.EventType.ShouldBe((short)EmailDeliveryEventType.Queued);
        row.Status.ShouldBe((short)EmailDeliveryStatus.Queued);
        row.OccurredAtUtc.ShouldBe(timeProvider.GetUtcNow());
        row.MessagePayload.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>When an attempt is recorded, then attempt details are stored.</summary>
    /// <intent>Capture provider attempt details.</intent>
    /// <scenario>Given a failed attempt.</scenario>
    /// <behavior>Stores status, attempt number, and provider details.</behavior>
    [Fact]
    public async Task RecordAttempt_WritesAttemptDetails()
    {
        var message = CreateMessage("attempt-1");
        var attemptTime = timeProvider.GetUtcNow().AddMinutes(5);
        var attempt = new EmailDeliveryAttempt(
            attemptNumber: 2,
            timestampUtc: attemptTime,
            status: EmailDeliveryStatus.FailedTransient,
            providerMessageId: "provider-1",
            errorCode: "E1",
            errorMessage: "Timeout");

        await sink!.RecordAttemptAsync(message, attempt, TestContext.Current.CancellationToken);

        var row = await GetByAttemptNumberAsync(2);
        row.EventType.ShouldBe((short)EmailDeliveryEventType.Attempt);
        row.Status.ShouldBe((short)EmailDeliveryStatus.FailedTransient);
        row.ProviderMessageId.ShouldBe("provider-1");
        row.ErrorCode.ShouldBe("E1");
        row.ErrorMessage.ShouldBe("Timeout");
        row.OccurredAtUtc.ShouldBe(attemptTime);
    }

    /// <summary>When an external update is recorded, then provider metadata is stored.</summary>
    /// <intent>Capture webhook delivery updates.</intent>
    /// <scenario>Given a provider event id.</scenario>
    /// <behavior>Stores provider event id and status.</behavior>
    [Fact]
    public async Task RecordExternal_WritesExternalUpdate()
    {
        var update = new EmailDeliveryUpdate(
            MessageKey: "external-1",
            ProviderMessageId: "provider-2",
            ProviderEventId: "evt-1",
            Status: EmailDeliveryStatus.Bounced,
            ErrorCode: "B1",
            ErrorMessage: "Bounce");

        await sink!.RecordExternalAsync(update, TestContext.Current.CancellationToken);

        var row = await GetByProviderEventIdAsync("evt-1");
        row.EventType.ShouldBe((short)EmailDeliveryEventType.External);
        row.Status.ShouldBe((short)EmailDeliveryStatus.Bounced);
        row.MessageKey.ShouldBe("external-1");
        row.ProviderMessageId.ShouldBe("provider-2");
        row.ErrorCode.ShouldBe("B1");
    }

    /// <summary>When a correlation context is present, then correlation fields are stored.</summary>
    /// <intent>Capture ambient correlation metadata.</intent>
    /// <scenario>Given a correlation context in scope.</scenario>
    /// <behavior>Stores correlation ids and tags.</behavior>
    [Fact]
    public async Task RecordQueued_StoresCorrelationContext()
    {
        var accessor = new AmbientCorrelationContextAccessor();
        accessor.Current = new CorrelationContext(
            new CorrelationId("corr-1"),
            new CorrelationId("cause-1"),
            "trace-1",
            "span-1",
            timeProvider.GetUtcNow(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["env"] = "test" });

        sink = new PostgresEmailDeliverySink(
            Options.Create(options),
            timeProvider,
            accessor,
            NullLogger<PostgresEmailDeliverySink>.Instance);

        var message = CreateMessage("corr-1");
        await sink.RecordQueuedAsync(message, TestContext.Current.CancellationToken);

        var row = await GetByMessageKeyAsync("corr-1");
        row.CorrelationId.ShouldBe("corr-1");
        row.CausationId.ShouldBe("cause-1");
        row.TraceId.ShouldBe("trace-1");
        row.SpanId.ShouldBe("span-1");
        row.CorrelationTagsJson.ShouldNotBeNullOrWhiteSpace();
    }

    private async Task<EmailDeliveryRow> GetByMessageKeyAsync(string messageKey)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await connection.QuerySingleAsync<EmailDeliveryRow>(
            $"""
            SELECT *
            FROM {qualifiedTable}
            WHERE "MessageKey" = @MessageKey
            """,
            new { MessageKey = messageKey });
    }

    private async Task<EmailDeliveryRow> GetByAttemptNumberAsync(int attemptNumber)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await connection.QuerySingleAsync<EmailDeliveryRow>(
            $"""
            SELECT *
            FROM {qualifiedTable}
            WHERE "AttemptNumber" = @AttemptNumber
            """,
            new { AttemptNumber = attemptNumber });
    }

    private async Task<EmailDeliveryRow> GetByProviderEventIdAsync(string providerEventId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await connection.QuerySingleAsync<EmailDeliveryRow>(
            $"""
            SELECT *
            FROM {qualifiedTable}
            WHERE "ProviderEventId" = @ProviderEventId
            """,
            new { ProviderEventId = providerEventId });
    }

    private static OutboundEmailMessage CreateMessage(string messageKey)
    {
        return new OutboundEmailMessage(
            messageKey,
            new EmailAddress("noreply@acme.test", "Acme"),
            new[] { new EmailAddress("user@acme.test") },
            "Hello",
            textBody: "Hello there",
            htmlBody: null);
    }

    private sealed class EmailDeliveryRow
    {
        public short EventType { get; init; }
        public short Status { get; init; }
        public DateTimeOffset OccurredAtUtc { get; init; }
        public string? MessageKey { get; init; }
        public string? ProviderMessageId { get; init; }
        public string? ProviderEventId { get; init; }
        public int? AttemptNumber { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public string? MessagePayload { get; init; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public string? TraceId { get; init; }
        public string? SpanId { get; init; }
        public string? CorrelationTagsJson { get; init; }
    }
}
