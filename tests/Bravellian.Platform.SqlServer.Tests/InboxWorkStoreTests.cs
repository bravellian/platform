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

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxWorkStoreTests : SqlServerTestBase
{
    public InboxWorkStoreTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure inbox work queue schema is set up (stored procedures and types)
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>When ClaimAsync is called with no pending messages, then it returns an empty set.</summary>
    /// <intent>Confirm the inbox work store returns no claims when there is no work.</intent>
    /// <scenario>Given an empty inbox table and a SqlInboxWorkStore instance.</scenario>
    /// <behavior>Then ClaimAsync returns an empty list of message ids.</behavior>
    [Fact]
    public async Task ClaimAsync_WithNoMessages_ReturnsEmpty()
    {
        // Arrange
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Act
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Empty(claimedIds);
    }

    /// <summary>When a ready message exists, then ClaimAsync claims it and marks it as processing.</summary>
    /// <intent>Verify claims update inbox status and owner token correctly.</intent>
    /// <scenario>Given an enqueued inbox message and a generated owner token.</scenario>
    /// <behavior>Then ClaimAsync returns the message id and the row is marked Processing with the owner token.</behavior>
    [Fact]
    public async Task ClaimAsync_WithAvailableMessage_ClaimsSuccessfully()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue a test message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Single(claimedIds);
        Assert.Contains(messageId, claimedIds);

        // Verify message status is Processing and has owner token
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(ownerToken.Value, (Guid)result.OwnerToken);
    }

    /// <summary>When two workers claim concurrently, then only one receives the message.</summary>
    /// <intent>Ensure exclusive claims under concurrent callers.</intent>
    /// <scenario>Given a single inbox message and two ClaimAsync calls with different owner tokens.</scenario>
    /// <behavior>Then exactly one claim list contains the message id.</behavior>
    [Fact]
    public async Task ClaimAsync_WithConcurrentWorkers_EnsuresExclusiveClaims()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();

        // Enqueue a single message
        await inbox.EnqueueAsync("test-topic", "test-source", InboxMessageIdentifier.From("msg-1"), "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act - Two workers try to claim the same message
        var claims1Task = store.ClaimAsync(owner1, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        var claims2Task = store.ClaimAsync(owner2, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        var claims1 = await claims1Task;
        var claims2 = await claims2Task;

        // Assert - Only one worker should get the message
        var totalClaimed = claims1.Count + claims2.Count;
        Assert.Equal(1, totalClaimed);

        // Verify exactly one claim succeeded
        Assert.True((claims1.Count == 1 && claims2.Count == 0) || (claims1.Count == 0 && claims2.Count == 1));
    }

    /// <summary>When AckAsync is called by the owner, then the message is marked Done and processed time is set.</summary>
    /// <intent>Verify successful acknowledgements update inbox state.</intent>
    /// <scenario>Given a claimed inbox message and the owning token.</scenario>
    /// <behavior>Then the row status is Done, OwnerToken is cleared, and ProcessedUtc is set.</behavior>
    [Fact]
    public async Task AckAsync_WithClaimedMessage_MarksAsDone()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AckAsync(ownerToken, claimedIds, CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken, ProcessedUtc FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Done", result.Status);
        Assert.Null(result.OwnerToken);
        Assert.NotNull(result.ProcessedUtc);
    }

    /// <summary>When AbandonAsync is called by the owner, then the message returns to Seen status.</summary>
    /// <intent>Verify abandoning a claim resets the inbox state.</intent>
    /// <scenario>Given a claimed inbox message and the owning token.</scenario>
    /// <behavior>Then the row status is Seen and OwnerToken is cleared.</behavior>
    [Fact]
    public async Task AbandonAsync_WithClaimedMessage_ReturnsToSeen()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AbandonAsync(ownerToken, claimedIds, cancellationToken: CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Seen", result.Status);
        Assert.Null(result.OwnerToken);
    }

    /// <summary>When FailAsync is called by the owner, then the message is marked Dead.</summary>
    /// <intent>Verify failed processing sets the inbox message to Dead.</intent>
    /// <scenario>Given a claimed inbox message and the owning token.</scenario>
    /// <behavior>Then the row status is Dead and OwnerToken is cleared.</behavior>
    [Fact]
    public async Task FailAsync_WithClaimedMessage_MarksAsDead()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.FailAsync(ownerToken, claimedIds, "Test failure", CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Dead", result.Status);
        Assert.Null(result.OwnerToken);
    }

    /// <summary>When a non-owner tries to acknowledge a claim, then the message remains in Processing.</summary>
    /// <intent>Ensure owner token enforcement prevents unauthorized state changes.</intent>
    /// <scenario>Given a message claimed by one owner and AckAsync called with a different owner token.</scenario>
    /// <behavior>Then the row status remains Processing with the original owner token.</behavior>
    [Fact]
    public async Task OwnerTokenEnforcement_OnlyAllowsOperationsByOwner()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var rightOwner = OwnerToken.GenerateNew();
        var wrongOwner = OwnerToken.GenerateNew();

        // Enqueue and claim a message with rightOwner
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(rightOwner, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act - Try to ack with wrong owner
        await store.AckAsync(wrongOwner, claimedIds, CancellationToken.None);

        // Assert - Message should still be Processing (ack should have been ignored)
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(rightOwner.Value, (Guid)result.OwnerToken);
    }

    /// <summary>When GetAsync is called with a valid message id, then it returns the stored message details.</summary>
    /// <intent>Verify inbox work store retrieval returns expected fields.</intent>
    /// <scenario>Given an enqueued inbox message with a known id.</scenario>
    /// <behavior>Then GetAsync returns a message with matching id, source, topic, payload, and attempt.</behavior>
    [Fact]
    public async Task GetAsync_WithValidMessageId_ReturnsMessage()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();

        // Enqueue a test message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var message = await store.GetAsync(messageId, CancellationToken.None);

        // Assert
        Assert.NotNull(message);
        Assert.Equal(messageId, message.MessageId);
        Assert.Equal("test-source", message.Source);
        Assert.Equal("test-topic", message.Topic);
        Assert.Equal("test payload", message.Payload);
        Assert.Equal(1, message.Attempt); // First attempt
    }

    /// <summary>When GetAsync is called with an unknown message id, then it throws an InvalidOperationException.</summary>
    /// <intent>Ensure missing inbox messages result in a failure.</intent>
    /// <scenario>Given a SqlInboxWorkStore with no message matching the requested id.</scenario>
    /// <behavior>Then GetAsync throws InvalidOperationException.</behavior>
    [Fact]
    public async Task GetAsync_WithInvalidMessageId_ThrowsException()
    {
        // Arrange
        var store = CreateInboxWorkStore();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync(InboxMessageIdentifier.From("non-existent"), CancellationToken.None));
    }

    private SqlInboxService CreateInboxService()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxService>(TestOutputHelper);
        return new SqlInboxService(options, logger);
    }

    private SqlInboxWorkStore CreateInboxWorkStore()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxWorkStore>(TestOutputHelper);
        return new SqlInboxWorkStore(options, TimeProvider.System, logger);
    }
}

