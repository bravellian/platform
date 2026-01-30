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

using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class GlobalControlPlaneRoutingTests
{
    private readonly PostgresCollectionFixture fixture;

    public GlobalControlPlaneRoutingTests(PostgresCollectionFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task GlobalAliases_RouteToControlPlane()
    {
        var tenant = new PlatformDatabase
        {
            Name = "tenant-1",
            ConnectionString = await fixture.CreateTestDatabaseAsync("tenant-routing").ConfigureAwait(false),
            SchemaName = "app",
        };
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("control-routing").ConfigureAwait(false);

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(controlPlaneConnection, "control", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(controlPlaneConnection, "control").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(controlPlaneConnection, "control", "Inbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(controlPlaneConnection, "control").ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(
            new[] { tenant },
            new PlatformControlPlaneOptions
            {
                ConnectionString = controlPlaneConnection,
                SchemaName = "control",
                EnableSchemaDeployment = false,
            });

        await using var provider = services.BuildServiceProvider();

        var globalOutbox = provider.GetRequiredService<IGlobalOutbox>();
        var globalInbox = provider.GetRequiredService<IGlobalInbox>();
        var globalInboxStore = provider.GetRequiredService<IGlobalInboxWorkStore>();
        provider.GetRequiredService<IGlobalSchedulerClient>().ShouldNotBeNull();
        provider.GetRequiredService<IGlobalSystemLeaseFactory>().ShouldNotBeNull();

        await globalOutbox.EnqueueAsync("global.topic", "payload", TestContext.Current.CancellationToken);

        var outboxCount = await CountOutboxAsync(controlPlaneConnection, "control", "global.topic").ConfigureAwait(false);
        outboxCount.ShouldBe(1);

        await globalInbox.EnqueueAsync(
            "global.inbox",
            "source",
            "global-msg-1",
            "payload",
            TestContext.Current.CancellationToken);

        var inboxMessage = await globalInboxStore.GetAsync("global-msg-1", TestContext.Current.CancellationToken);
        inboxMessage.Topic.ShouldBe("global.inbox");
        inboxMessage.Source.ShouldBe("source");
    }

    private static async Task<int> CountOutboxAsync(string connectionString, string schemaName, string topic)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{schemaName}\".\"Outbox\" WHERE \"Topic\" = @topic";
        command.Parameters.AddWithValue("topic", topic);
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
