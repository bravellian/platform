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
        SchemaName = "dbo", 
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
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "dbo", "Outbox");
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "dbo");
        
        joinStore = new SqlOutboxJoinStore(
            Options.Create(defaultOptions), 
            NullLogger<SqlOutboxJoinStore>.Instance);
            
        outbox = new SqlOutboxService(
            Options.Create(defaultOptions), 
            NullLogger<SqlOutboxService>.Instance,
            joinStore);
    }

    [Fact]
    public async Task EnqueueJoinWaitAsync_WithAllParameters_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = Guid.NewGuid();
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
        
        var message = await connection.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT TOP 1 Payload FROM dbo.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");

        message.ShouldNotBeNull();
        
        var payload = JsonSerializer.Deserialize<JoinWaitPayload>((string)message.Payload);
        payload.ShouldNotBeNull();
        payload!.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeTrue();
        payload.OnCompleteTopic.ShouldBe(onCompleteTopic);
        payload.OnCompletePayload.ShouldBe(onCompletePayload);
        payload.OnFailTopic.ShouldBe(onFailTopic);
        payload.OnFailPayload.ShouldBe(onFailPayload);
    }

    [Fact]
    public async Task EnqueueJoinWaitAsync_WithMinimalParameters_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = Guid.NewGuid();

        // Act
        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            cancellationToken: CancellationToken.None);

        // Assert - verify message was enqueued with defaults
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        
        var message = await connection.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT TOP 1 Payload FROM dbo.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");

        message.ShouldNotBeNull();
        
        var payload = JsonSerializer.Deserialize<JoinWaitPayload>((string)message.Payload);
        payload.ShouldNotBeNull();
        payload!.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeTrue(); // Default value
        payload.OnCompleteTopic.ShouldBeNull();
        payload.OnCompletePayload.ShouldBeNull();
        payload.OnFailTopic.ShouldBeNull();
        payload.OnFailPayload.ShouldBeNull();
    }

    [Fact]
    public async Task EnqueueJoinWaitAsync_WithFailIfAnyStepFailedFalse_EnqueuesCorrectMessage()
    {
        // Arrange
        var joinId = Guid.NewGuid();

        // Act
        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            failIfAnyStepFailed: false,
            onCompleteTopic: "complete",
            cancellationToken: CancellationToken.None);

        // Assert
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        
        var message = await connection.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT TOP 1 Payload FROM dbo.Outbox WHERE Topic = 'join.wait' ORDER BY CreatedAt DESC");

        message.ShouldNotBeNull();
        
        var payload = JsonSerializer.Deserialize<JoinWaitPayload>((string)message.Payload);
        payload.ShouldNotBeNull();
        payload!.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeFalse();
        payload.OnCompleteTopic.ShouldBe("complete");
    }
}
