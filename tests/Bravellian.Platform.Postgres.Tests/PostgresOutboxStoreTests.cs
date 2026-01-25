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
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class PostgresOutboxStoreTests : PostgresTestBase
{
    private PostgresOutboxStore? outboxStore;
    private readonly PostgresOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox" };
    private FakeTimeProvider timeProvider = default!;
    private string qualifiedTableName = string.Empty;

    public PostgresOutboxStoreTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        timeProvider = new FakeTimeProvider();
        defaultOptions.ConnectionString = ConnectionString;
        qualifiedTableName = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);
        var logger = new TestLogger<PostgresOutboxStore>(TestOutputHelper);
        outboxStore = new PostgresOutboxStore(Options.Create(defaultOptions), timeProvider, logger);
    }

    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimDueAsync_WithDueMessages_ReturnsMessages()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(1);
        messages.First().Id.ShouldBe(OutboxWorkItemIdentifier.From(messageId));
        messages.First().Topic.ShouldBe("Test.Topic");
        messages.First().Payload.ShouldBe("test payload");
    }

    [Fact]
    public async Task ClaimDueAsync_WithFutureMessages_ReturnsEmpty()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount", "DueTimeUtc")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @DueTimeUtc)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                DueTimeUtc = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task MarkDispatchedAsync_UpdatesMessage()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        await outboxStore!.MarkDispatchedAsync(OutboxWorkItemIdentifier.From(messageId), CancellationToken.None);

        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "Status", "IsProcessed"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """, new { Id = messageId });

        ((short)result.Status).ShouldBe((short)OutboxStatus.Done);
        ((bool)result.IsProcessed).ShouldBeTrue();
    }

    [Fact]
    public async Task RescheduleAsync_UpdatesRetryCountAndNextAttempt()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 2)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        var delay = TimeSpan.FromMinutes(5);
        var errorMessage = "Test error";

        await outboxStore!.RescheduleAsync(OutboxWorkItemIdentifier.From(messageId), delay, errorMessage, CancellationToken.None);

        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "RetryCount", "LastError", "Status"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """, new { Id = messageId });

        ((int)result.RetryCount).ShouldBe(3);
        ((string)result.LastError).ShouldBe(errorMessage);
        ((short)result.Status).ShouldBe((short)OutboxStatus.Ready);
    }

    [Fact]
    public async Task FailAsync_MarksMessageAsFailed()
    {
        var messageId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)
            """,
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        var errorMessage = "Permanent failure";

        await outboxStore!.FailAsync(OutboxWorkItemIdentifier.From(messageId), errorMessage, CancellationToken.None);

        var result = await connection.QueryFirstAsync(
            $"""
            SELECT "Status", "LastError", "ProcessedBy"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """, new { Id = messageId });

        ((short)result.Status).ShouldBe((short)OutboxStatus.Failed);
        ((string)result.LastError).ShouldBe(errorMessage);
        ((string)result.ProcessedBy).ShouldContain("FAILED");
    }
}
