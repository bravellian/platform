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

    /// <summary>
    /// Given an empty inbox, then ClaimAsync returns no message ids.
    /// </summary>
    /// <intent>
    /// Verify claim behavior when no work is available.
    /// </intent>
    /// <scenario>
    /// Given an inbox work store with no enqueued messages.
    /// </scenario>
    /// <behavior>
    /// The claimed id list is empty.
    /// </behavior>
    [Fact]
    public async Task ClaimAsync_WithNoMessages_ReturnsEmpty()
    {
        var store = CreateInboxWorkStore();
        var ownerToken = OwnerToken.GenerateNew();

        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        Assert.Empty(claimedIds);
    }

    /// <summary>
    /// When a message is available, then ClaimAsync claims it and marks it Processing.
    /// </summary>
    /// <intent>
    /// Verify claims update inbox status and ownership.
    /// </intent>
    /// <scenario>
    /// Given one enqueued message and a new owner token.
    /// </scenario>
    /// <behavior>
    /// The message id is returned and the row shows Status Processing with the owner token.
    /// </behavior>
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

    /// <summary>
    /// When two workers claim concurrently, then only one receives the message.
    /// </summary>
    /// <intent>
    /// Verify claim exclusivity under concurrent workers.
    /// </intent>
    /// <scenario>
    /// Given one enqueued message and two owner tokens claiming at the same time.
    /// </scenario>
    /// <behavior>
    /// Exactly one claim result contains the message id.
    /// </behavior>
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

    /// <summary>
    /// When acknowledging a claimed message, then its status becomes Done.
    /// </summary>
    /// <intent>
    /// Verify acknowledgements finalize the inbox row.
    /// </intent>
    /// <scenario>
    /// Given a message claimed by an owner token.
    /// </scenario>
    /// <behavior>
    /// Status is Done, OwnerToken is cleared, and ProcessedUtc is set.
    /// </behavior>
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

    /// <summary>
    /// When abandoning a claimed message, then its status returns to Seen.
    /// </summary>
    /// <intent>
    /// Verify abandon releases the claim and resets the status.
    /// </intent>
    /// <scenario>
    /// Given a message claimed by an owner token.
    /// </scenario>
    /// <behavior>
    /// Status is Seen and OwnerToken is cleared.
    /// </behavior>
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

    /// <summary>
    /// When failing a claimed message, then its status becomes Dead.
    /// </summary>
    /// <intent>
    /// Verify failure handling marks the message as Dead.
    /// </intent>
    /// <scenario>
    /// Given a message claimed by an owner token and a failure reason.
    /// </scenario>
    /// <behavior>
    /// Status is Dead and OwnerToken is cleared.
    /// </behavior>
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

    /// <summary>
    /// When a non-owner attempts to acknowledge a claim, then the row remains Processing.
    /// </summary>
    /// <intent>
    /// Verify owner token enforcement for state transitions.
    /// </intent>
    /// <scenario>
    /// Given a message claimed by one owner and an ack attempt by a different owner.
    /// </scenario>
    /// <behavior>
    /// Status stays Processing and the OwnerToken remains the original owner.
    /// </behavior>
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

    /// <summary>
    /// Given a valid message id, then GetAsync returns the stored inbox message.
    /// </summary>
    /// <intent>
    /// Verify message retrieval by id returns stored fields.
    /// </intent>
    /// <scenario>
    /// Given one enqueued message with id "msg-1".
    /// </scenario>
    /// <behavior>
    /// The returned message matches the stored fields and has Attempt 1.
    /// </behavior>
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

    /// <summary>
    /// When requesting a missing message id, then GetAsync throws InvalidOperationException.
    /// </summary>
    /// <intent>
    /// Verify missing messages raise an exception.
    /// </intent>
    /// <scenario>
    /// Given an inbox store with no message matching "non-existent".
    /// </scenario>
    /// <behavior>
    /// An InvalidOperationException is thrown.
    /// </behavior>
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

