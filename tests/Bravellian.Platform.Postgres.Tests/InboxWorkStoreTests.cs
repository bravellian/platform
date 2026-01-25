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

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxWorkStoreTests : PostgresTestBase
{
    private readonly string qualifiedInboxTableName = PostgresSqlHelper.Qualify("infra", "Inbox");

    public InboxWorkStoreTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task ClaimAsync_WithNoMessages_ReturnsEmpty()
    {
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        Assert.Empty(claimedIds);
    }

    [Fact]
    public async Task ClaimAsync_WithAvailableMessage_ClaimsSuccessfully()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        Assert.Single(claimedIds);
        Assert.Contains("msg-1", claimedIds);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            $"SELECT \"Status\", \"OwnerToken\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(ownerToken.Value, (Guid)result.OwnerToken);
    }

    [Fact]
    public async Task ClaimAsync_WithConcurrentWorkers_EnsuresExclusiveClaims()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        var claims1Task = store.ClaimAsync(owner1, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        var claims2Task = store.ClaimAsync(owner2, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        var claims1 = await claims1Task;
        var claims2 = await claims2Task;

        Assert.True((claims1.Count == 1 && claims2.Count == 0) || (claims1.Count == 0 && claims2.Count == 1));
    }

    [Fact]
    public async Task AckAsync_WithClaimedMessage_MarksAsDone()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        await store.AckAsync(ownerToken, claimedIds, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            $"SELECT \"Status\", \"OwnerToken\", \"ProcessedUtc\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Done", result.Status);
        Assert.Null(result.OwnerToken);
        Assert.NotNull(result.ProcessedUtc);
    }

    [Fact]
    public async Task AbandonAsync_WithClaimedMessage_ReturnsToSeen()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        await store.AbandonAsync(ownerToken, claimedIds, cancellationToken: CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            $"SELECT \"Status\", \"OwnerToken\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Seen", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task FailAsync_WithClaimedMessage_MarksAsDead()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        await store.FailAsync(ownerToken, claimedIds, "Test failure", CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            $"SELECT \"Status\", \"OwnerToken\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Dead", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task OwnerTokenEnforcement_OnlyAllowsOperationsByOwner()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var rightOwner = OwnerToken.GenerateNew();
        var wrongOwner = OwnerToken.GenerateNew();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(rightOwner, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        await store.AckAsync(wrongOwner, claimedIds, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            $"SELECT \"Status\", \"OwnerToken\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(rightOwner.Value, (Guid)result.OwnerToken);
    }

    [Fact]
    public async Task GetAsync_WithValidMessageId_ReturnsMessage()
    {
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();

        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        var message = await store.GetAsync("msg-1", CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("msg-1", message.MessageId);
        Assert.Equal("test-source", message.Source);
        Assert.Equal("test-topic", message.Topic);
        Assert.Equal("test payload", message.Payload);
        Assert.Equal(1, message.Attempt);
    }

    [Fact]
    public async Task GetAsync_WithInvalidMessageId_ThrowsException()
    {
        var store = CreateInboxWorkStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync("non-existent", CancellationToken.None));
    }

    private PostgresInboxService CreateInboxService()
    {
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
        return new PostgresInboxService(options, logger);
    }

    private PostgresInboxWorkStore CreateInboxWorkStore()
    {
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxWorkStore>(TestOutputHelper);
        return new PostgresInboxWorkStore(options, TimeProvider.System, logger);
    }
}
