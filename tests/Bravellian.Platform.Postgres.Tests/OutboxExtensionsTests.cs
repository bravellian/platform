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
public class OutboxExtensionsTests : PostgresTestBase
{
    private PostgresOutboxJoinStore? joinStore;
    private PostgresOutboxService? outbox;
    private readonly PostgresOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox",
    };
    private string qualifiedOutboxTable = string.Empty;

    public OutboxExtensionsTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
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
    }

    /// <summary>When EnqueueJoinWaitAsync is called with full parameters, then the join.wait message contains them.</summary>
    /// <intent>Verify join-wait payload fields are populated from all provided parameters.</intent>
    /// <scenario>Given a join id with on-complete and on-fail topics/payloads.</scenario>
    /// <behavior>The stored payload matches the join id, flags, and on-complete/on-fail values.</behavior>
    [Fact]
    public async Task EnqueueJoinWaitAsync_WithAllParameters_EnqueuesCorrectMessage()
    {
        var joinId = JoinIdentifier.GenerateNew();
        var onCompleteTopic = "etl.transform";
        var onCompletePayload = """{"transformId": "123"}""";
        var onFailTopic = "notify.failure";
        var onFailPayload = """{"reason": "Failed"}""";

        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            failIfAnyStepFailed: true,
            onCompleteTopic: onCompleteTopic,
            onCompletePayload: onCompletePayload,
            onFailTopic: onFailTopic,
            onFailPayload: onFailPayload,
            cancellationToken: CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var payloadText = await connection.QuerySingleOrDefaultAsync<string>(
            $"SELECT \"Payload\" FROM {qualifiedOutboxTable} WHERE \"Topic\" = 'join.wait' ORDER BY \"CreatedAt\" DESC LIMIT 1");
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

    /// <summary>When EnqueueJoinWaitAsync is called with minimal parameters, then optional fields remain null.</summary>
    /// <intent>Verify defaults are applied when optional join-wait parameters are omitted.</intent>
    /// <scenario>Given only a join id passed to EnqueueJoinWaitAsync.</scenario>
    /// <behavior>The payload has the join id, FailIfAnyStepFailed true, and null optional fields.</behavior>
    [Fact]
    public async Task EnqueueJoinWaitAsync_WithMinimalParameters_EnqueuesCorrectMessage()
    {
        var joinId = JoinIdentifier.GenerateNew();

        await outbox!.EnqueueJoinWaitAsync(
            joinId: joinId,
            cancellationToken: CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var payloadText = await connection.QuerySingleOrDefaultAsync<string>(
            $"SELECT \"Payload\" FROM {qualifiedOutboxTable} WHERE \"Topic\" = 'join.wait' ORDER BY \"CreatedAt\" DESC LIMIT 1");
        Assert.NotNull(payloadText);

        var payload = JsonSerializer.Deserialize<JoinWaitPayload>(payloadText);
        Assert.NotNull(payload);
        payload.JoinId.ShouldBe(joinId);
        payload.FailIfAnyStepFailed.ShouldBeTrue();
        payload.OnCompleteTopic.ShouldBeNull();
        payload.OnCompletePayload.ShouldBeNull();
        payload.OnFailTopic.ShouldBeNull();
        payload.OnFailPayload.ShouldBeNull();
    }
}

