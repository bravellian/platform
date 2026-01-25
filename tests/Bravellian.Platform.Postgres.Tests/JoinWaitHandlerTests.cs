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

using System.Text.Json;
using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class JoinWaitHandlerTests : PostgresTestBase
{
    private PostgresOutboxJoinStore? joinStore;
    private PostgresOutboxService? outbox;
    private JoinWaitHandler? handler;
    private readonly PostgresOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox",
    };
    private string qualifiedOutboxTable = string.Empty;

    public JoinWaitHandlerTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        qualifiedOutboxTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        joinStore = new PostgresOutboxJoinStore(
            Options.Create(defaultOptions),
            NullLogger<PostgresOutboxJoinStore>.Instance);

        outbox = new PostgresOutboxService(
            Options.Create(defaultOptions),
            NullLogger<PostgresOutboxService>.Instance,
            joinStore);

        handler = new JoinWaitHandler(
            joinStore,
            NullLogger<JoinWaitHandler>.Instance,
            outbox);
    }

    private async Task<OutboxMessageIdentifier> CreateOutboxMessageAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            $"INSERT INTO {qualifiedOutboxTable} (\"Id\", \"Topic\", \"Payload\", \"MessageId\") VALUES (@Id, @Topic, @Payload, @MessageId)",
            new { Id = id, Topic = "test.topic", Payload = "{}", MessageId = Guid.NewGuid() }).ConfigureAwait(false);

        return OutboxMessageIdentifier.From(id);
    }

    [Fact]
    public async Task HandleAsync_WhenJoinNotReady_ThrowsJoinNotReadyException()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true,
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew(),
        };

        await Should.ThrowAsync<JoinNotReadyException>(async () =>
            await handler!.HandleAsync(message, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task HandleAsync_WhenAllStepsCompleted_MarksJoinAsCompleted()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var message1 = await CreateOutboxMessageAsync();
        var message2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, message1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, message2, CancellationToken.None);

        await joinStore.IncrementCompletedAsync(join.JoinId, message1, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, message2, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true,
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew(),
        };

        await handler!.HandleAsync(message, CancellationToken.None).ConfigureAwait(false);

        var updated = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updated.ShouldNotBeNull();
        updated!.CompletedSteps.ShouldBe(2);
        updated.ExpectedSteps.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_WhenAnyStepFailed_MarksJoinFailed()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var message1 = await CreateOutboxMessageAsync();
        var message2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, message1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, message2, CancellationToken.None);

        await joinStore.IncrementCompletedAsync(join.JoinId, message1, CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, message2, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true,
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew(),
        };

        await handler!.HandleAsync(message, CancellationToken.None).ConfigureAwait(false);

        var updated = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None).ConfigureAwait(false);
        updated.ShouldNotBeNull();
        updated!.Status.ShouldBe(JoinStatus.Failed);
    }
}
