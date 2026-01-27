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
public sealed class PostgresEmailOutboxStoreTests : PostgresTestBase
{
    private PostgresEmailOutboxStore? store;
    private PostgresEmailOutboxOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "EmailOutbox",
    };
    private FakeTimeProvider timeProvider = default!;
    private string qualifiedTable = string.Empty;

    public PostgresEmailOutboxStoreTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureEmailOutboxSchemaAsync(ConnectionString).ConfigureAwait(false);

        timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        options.ConnectionString = ConnectionString;
        qualifiedTable = PostgresSqlHelper.Qualify(options.SchemaName, options.TableName);
        store = new PostgresEmailOutboxStore(Options.Create(options), timeProvider, NullLogger<PostgresEmailOutboxStore>.Instance);
    }

    /// <summary>When enqueue is called, then the provider/message key is recorded.</summary>
    /// <intent>Verify idempotency key tracking.</intent>
    /// <scenario>Given an empty outbox and a new message.</scenario>
    /// <behavior>AlreadyEnqueued returns true after enqueue.</behavior>
    [Fact]
    public async Task Enqueue_MarksMessageKey()
    {
        var message = CreateMessage("key-1");
        var item = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            message.MessageKey,
            message,
            timeProvider.GetUtcNow(),
            null,
            0);

        var before = await store!.AlreadyEnqueuedAsync(message.MessageKey, "postmark", TestContext.Current.CancellationToken);
        before.ShouldBeFalse();

        await store.EnqueueAsync(item, TestContext.Current.CancellationToken);

        var after = await store.AlreadyEnqueuedAsync(message.MessageKey, "postmark", TestContext.Current.CancellationToken);
        after.ShouldBeTrue();
    }

    /// <summary>When dequeue is called, then pending items are returned in enqueue order.</summary>
    /// <intent>Ensure due items are claimed in FIFO order.</intent>
    /// <scenario>Given two pending messages.</scenario>
    /// <behavior>Returns items ordered by EnqueuedAtUtc and increments attempt count.</behavior>
    [Fact]
    public async Task Dequeue_ReturnsPendingItemsInOrder()
    {
        var first = CreateMessage("key-1", "Hello 1");
        var second = CreateMessage("key-2", "Hello 2");
        var firstItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            first.MessageKey,
            first,
            timeProvider.GetUtcNow().AddMinutes(-1),
            null,
            0);
        var secondItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            second.MessageKey,
            second,
            timeProvider.GetUtcNow(),
            null,
            0);

        await store!.EnqueueAsync(secondItem, TestContext.Current.CancellationToken);
        await store.EnqueueAsync(firstItem, TestContext.Current.CancellationToken);

        var batch = await store.DequeueAsync(10, TestContext.Current.CancellationToken);

        batch.Count.ShouldBe(2);
        batch[0].Id.ShouldBe(firstItem.Id);
        batch[0].AttemptCount.ShouldBe(1);
    }

    /// <summary>When enqueue is called twice for the same provider/key, then only one row is stored.</summary>
    /// <intent>Verify idempotent enqueue behavior.</intent>
    /// <scenario>Given a duplicate message key for the same provider.</scenario>
    /// <behavior>Only one row exists in the outbox table.</behavior>
    [Fact]
    public async Task Enqueue_IsIdempotentForProviderAndMessageKey()
    {
        var message = CreateMessage("key-dup");
        var item = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            message.MessageKey,
            message,
            timeProvider.GetUtcNow(),
            null,
            0);

        await store!.EnqueueAsync(item, TestContext.Current.CancellationToken);
        var duplicate = new EmailOutboxItem(
            Guid.NewGuid(),
            item.ProviderName,
            item.MessageKey,
            item.Message,
            item.EnqueuedAtUtc,
            item.DueTimeUtc,
            item.AttemptCount);
        await store.EnqueueAsync(duplicate, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(1)
            FROM {qualifiedTable}
            WHERE "ProviderName" = @Provider AND "MessageKey" = @MessageKey
            """,
            new { Provider = item.ProviderName, item.MessageKey });

        count.ShouldBe(1);
    }

    private static OutboundEmailMessage CreateMessage(string messageKey, string subject = "Hello")
    {
        return new OutboundEmailMessage(
            messageKey,
            new EmailAddress("noreply@acme.test", "Acme"),
            new[] { new EmailAddress("user@acme.test") },
            subject,
            textBody: "Hello there",
            htmlBody: null);
    }
}
