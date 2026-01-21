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

    [Fact]
    public async Task ClaimAsync_WithAvailableMessage_ClaimsSuccessfully()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue a test message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Single(claimedIds);
        Assert.Contains("msg-1", claimedIds);

        // Verify message status is Processing and has owner token
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(ownerToken.Value, (Guid)result.OwnerToken);
    }

    [Fact]
    public async Task ClaimAsync_WithConcurrentWorkers_EnsuresExclusiveClaims()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();

        // Enqueue a single message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task AckAsync_WithClaimedMessage_MarksAsDone()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AckAsync(ownerToken, claimedIds, CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken, ProcessedUtc FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Done", result.Status);
        Assert.Null(result.OwnerToken);
        Assert.NotNull(result.ProcessedUtc);
    }

    [Fact]
    public async Task AbandonAsync_WithClaimedMessage_ReturnsToSeen()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AbandonAsync(ownerToken, claimedIds, cancellationToken: CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Seen", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task FailAsync_WithClaimedMessage_MarksAsDead()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.FailAsync(ownerToken, claimedIds, "Test failure", CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Dead", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task OwnerTokenEnforcement_OnlyAllowsOperationsByOwner()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var rightOwner = OwnerToken.GenerateNew();
        var wrongOwner = OwnerToken.GenerateNew();

        // Enqueue and claim a message with rightOwner
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);
        var claimedIds = await store.ClaimAsync(rightOwner, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act - Try to ack with wrong owner
        await store.AckAsync(wrongOwner, claimedIds, CancellationToken.None);

        // Assert - Message should still be Processing (ack should have been ignored)
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });

        Assert.Equal("Processing", result.Status);
        Assert.Equal(rightOwner.Value, (Guid)result.OwnerToken);
    }

    [Fact]
    public async Task GetAsync_WithValidMessageId_ReturnsMessage()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();

        // Enqueue a test message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var message = await store.GetAsync("msg-1", CancellationToken.None);

        // Assert
        Assert.NotNull(message);
        Assert.Equal("msg-1", message.MessageId);
        Assert.Equal("test-source", message.Source);
        Assert.Equal("test-topic", message.Topic);
        Assert.Equal("test payload", message.Payload);
        Assert.Equal(1, message.Attempt); // First attempt
    }

    [Fact]
    public async Task GetAsync_WithInvalidMessageId_ThrowsException()
    {
        // Arrange
        var store = CreateInboxWorkStore();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync("non-existent", CancellationToken.None));
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
