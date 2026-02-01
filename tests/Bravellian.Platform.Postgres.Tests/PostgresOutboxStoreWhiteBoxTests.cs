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

using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class PostgresOutboxStoreWhiteBoxTests : PostgresTestBase
{
    private readonly PostgresOutboxOptions options = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox", EnableSchemaDeployment = false };
    private PostgresOutboxStore? store;
    private FakeTimeProvider timeProvider = default!;
    private string qualifiedTableName = string.Empty;

    public PostgresOutboxStoreWhiteBoxTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        timeProvider = new FakeTimeProvider();
        options.ConnectionString = ConnectionString;
        qualifiedTableName = PostgresSqlHelper.Qualify(options.SchemaName, options.TableName);
        store = new PostgresOutboxStore(Options.Create(options), timeProvider, NullLogger<PostgresOutboxStore>.Instance);
    }

    /// <summary>When mark Dispatched Async Sets Processed Flags, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for mark Dispatched Async Sets Processed Flags.</intent>
    /// <scenario>Given mark Dispatched Async Sets Processed Flags.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task MarkDispatchedAsync_SetsProcessedFlags()
    {
        var messageId = await InsertMessageAsync();

        await store!.ClaimDueAsync(10, TestContext.Current.CancellationToken);
        await store.MarkDispatchedAsync(OutboxWorkItemIdentifier.From(messageId), TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "Status", "IsProcessed", "ProcessedAt"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """,
            new { Id = messageId });

        ((short)result.Status).ShouldBe((short)OutboxStatus.Done);
        ((bool)result.IsProcessed).ShouldBeTrue();
        ((DateTimeOffset?)result.ProcessedAt).ShouldNotBeNull();
    }

    /// <summary>When fail Async Sets Failure Metadata, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for fail Async Sets Failure Metadata.</intent>
    /// <scenario>Given fail Async Sets Failure Metadata.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task FailAsync_SetsFailureMetadata()
    {
        var messageId = await InsertMessageAsync();

        await store!.ClaimDueAsync(10, TestContext.Current.CancellationToken);

        const string errorMessage = "Permanent failure";
        await store.FailAsync(OutboxWorkItemIdentifier.From(messageId), errorMessage, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "Status", "LastError", "ProcessedBy"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """,
            new { Id = messageId });

        ((short)result.Status).ShouldBe((short)OutboxStatus.Failed);
        ((string)result.LastError).ShouldBe(errorMessage);
        ((string)result.ProcessedBy).ShouldContain("FAILED");
    }

    /// <summary>When reschedule Async Increments Retry Count And Sets Error, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for reschedule Async Increments Retry Count And Sets Error.</intent>
    /// <scenario>Given reschedule Async Increments Retry Count And Sets Error.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task RescheduleAsync_IncrementsRetryCountAndSetsError()
    {
        var messageId = await InsertMessageAsync();

        await store!.ClaimDueAsync(10, TestContext.Current.CancellationToken);

        const string errorMessage = "Reschedule error";
        await store.RescheduleAsync(OutboxWorkItemIdentifier.From(messageId), TimeSpan.Zero, errorMessage, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "RetryCount", "LastError", "Status", "DueTimeUtc"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """,
            new { Id = messageId });

        ((int)result.RetryCount).ShouldBe(1);
        ((string)result.LastError).ShouldBe(errorMessage);
        ((short)result.Status).ShouldBe((short)OutboxStatus.Ready);
        ((DateTimeOffset?)result.DueTimeUtc).ShouldNotBeNull();
    }

    private async Task<Guid> InsertMessageAsync()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount", "MessageId")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @MessageId)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
                MessageId = Guid.NewGuid(),
            });

        return messageId;
    }
}
