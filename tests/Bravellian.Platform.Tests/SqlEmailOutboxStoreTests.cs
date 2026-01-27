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
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SqlEmailOutboxStoreTests : SqlServerTestBase
{
    private SqlEmailOutboxStore? store;
    private SqlEmailOutboxOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "EmailOutbox",
    };
    private FakeTimeProvider timeProvider = default!;
    private string qualifiedTable = string.Empty;

    public SqlEmailOutboxStoreTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureEmailOutboxSchemaAsync(ConnectionString).ConfigureAwait(false);

        timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        options.ConnectionString = ConnectionString;
        qualifiedTable = $"[{options.SchemaName}].[{options.TableName}]";
        store = new SqlEmailOutboxStore(Options.Create(options), timeProvider, NullLogger<SqlEmailOutboxStore>.Instance);
    }

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

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM {qualifiedTable} WHERE ProviderName = @Provider AND MessageKey = @MessageKey",
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
