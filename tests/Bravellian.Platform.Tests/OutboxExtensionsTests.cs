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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxExtensionsTests : SqlServerTestBase
{
    private SqlOutboxJoinStore? joinStore;
    private SqlOutboxService? outbox;
    private readonly SqlOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox"
    };

    public OutboxExtensionsTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;

        // Ensure schemas exist
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        joinStore = new SqlOutboxJoinStore(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxJoinStore>.Instance);

        outbox = new SqlOutboxService(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxService>.Instance,
            joinStore);
    }

    /// <summary>When EnqueueJoinWaitAsync is called with all parameters, then the join.wait payload persists those values.</summary>
    /// <intent>Verify join wait enqueue serializes all provided topics, payloads, and flags.</intent>
    /// <scenario>Given an outbox service with join support and explicit on-complete/on-fail details.</scenario>
    /// <behavior>Then the persisted join.wait payload matches all provided parameters.</behavior>
    [Fact]
    public async Task EnqueueJoinWaitAsync_WithAllParameters_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = JoinIdentifier.GenerateNew();
        var onCompleteTopic = "etl.transform";
        var onCompletePayload = """{"transformId": "123"}""";
        var onFailTopic = "notify.failure";
        var onFailPayload = """{"reason": "Failed"}""";

        // Act
        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            failIfAnyStepFailed: true,
            onCompleteTopic: onCompleteTopic,
            onCompletePayload: onCompletePayload,
            onFailTopic: onFailTopic,
            onFailPayload: onFailPayload,
            cancellationToken: CancellationToken.None);

        // Assert - verify message was enqueued with correct payload
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var payloadText = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT TOP 1 Payload FROM infra.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");
        Assert.NotNull(payloadText);

        var payload = JsonSerializer.Deserialize<JoinWaitPayload>(payloadText);
        Assert.NotNull(payload);
        payload.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeTrue();
        payload.OnCompleteTopic.ShouldBe(onCompleteTopic);
        payload.OnCompletePayload.ShouldBe(onCompletePayload);
        payload.OnFailTopic.ShouldBe(onFailTopic);
        payload.OnFailPayload.ShouldBe(onFailPayload);
    }

    /// <summary>When EnqueueJoinWaitAsync is called with only a join id, then defaults are applied in the payload.</summary>
    /// <intent>Confirm minimal arguments still produce a valid join.wait payload.</intent>
    /// <scenario>Given an outbox service and a generated JoinIdentifier.</scenario>
    /// <behavior>Then the payload has FailIfAnyStepFailed = true and null on-complete/on-fail fields.</behavior>
    [Fact]
    public async Task EnqueueJoinWaitAsync_WithMinimalParameters_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = JoinIdentifier.GenerateNew();

        // Act
        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            cancellationToken: CancellationToken.None);

        // Assert - verify message was enqueued with defaults
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var payloadText = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT TOP 1 Payload FROM infra.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");
        Assert.NotNull(payloadText);

        var payload = JsonSerializer.Deserialize<JoinWaitPayload>(payloadText);
        Assert.NotNull(payload);
        payload.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeTrue(); // Default value
        payload.OnCompleteTopic.ShouldBeNull();
        payload.OnCompletePayload.ShouldBeNull();
        payload.OnFailTopic.ShouldBeNull();
        payload.OnFailPayload.ShouldBeNull();
    }

    /// <summary>When failIfAnyStepFailed is false, then the payload preserves the false flag and provided completion topic.</summary>
    /// <intent>Ensure the fail-if-any-step flag is stored as provided.</intent>
    /// <scenario>Given an outbox service and EnqueueJoinWaitAsync called with failIfAnyStepFailed = false.</scenario>
    /// <behavior>Then the join.wait payload has FailIfAnyStepFailed = false and the completion topic set.</behavior>
    [Fact]
    public async Task EnqueueJoinWaitAsync_WithFailIfAnyStepFailedFalse_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = JoinIdentifier.GenerateNew();

        // Act
        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            failIfAnyStepFailed: false,
            onCompleteTopic: "complete",
            cancellationToken: CancellationToken.None);

        // Assert
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var payloadText = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT TOP 1 Payload FROM infra.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");
        Assert.NotNull(payloadText);

        var payload = JsonSerializer.Deserialize<JoinWaitPayload>(payloadText);
        Assert.NotNull(payload);
        payload.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeFalse();
        payload.OnCompleteTopic.ShouldBe("complete");
    }
}
