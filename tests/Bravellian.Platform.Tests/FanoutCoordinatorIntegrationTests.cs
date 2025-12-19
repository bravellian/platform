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

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class FanoutCoordinatorIntegrationTests : SqlServerTestBase
{
    private SqlOutboxOptions? outboxOptions;
    private SqlFanoutOptions? fanoutOptions;
    private SqlOutboxJoinStore? joinStore;
    private SqlOutboxService? outboxService;

    public FanoutCoordinatorIntegrationTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        outboxOptions = new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox",
        };

        fanoutOptions = new SqlFanoutOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            PolicyTableName = "FanoutPolicy",
            CursorTableName = "FanoutCursor",
        };

        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            ConnectionString,
            fanoutOptions.SchemaName,
            fanoutOptions.PolicyTableName,
            fanoutOptions.CursorTableName).ConfigureAwait(false);

        joinStore = new SqlOutboxJoinStore(
            Options.Create(outboxOptions),
            NullLogger<SqlOutboxJoinStore>.Instance);

        outboxService = new SqlOutboxService(
            Options.Create(outboxOptions),
            NullLogger<SqlOutboxService>.Instance,
            joinStore);
    }

    [Fact]
    public async Task RunAsync_RespectsActiveLeaseAndRecoversAfterExpiry()
    {
        var leaseFactory = new InMemoryLeaseFactory(TimeSpan.FromMilliseconds(200));
        await using var heldLease = await leaseFactory.AcquireAsync(
            "fanout:billing",
            TimeSpan.FromMinutes(1),
            cancellationToken: TestContext.Current.CancellationToken);

        var planner = new StaticPlanner(new[] { new FanoutSlice("billing", "tenant-1", "daily") });
        var dispatcher = new FanoutDispatcher(outboxService!);
        var coordinator = new FanoutCoordinator(planner, dispatcher, leaseFactory, NullLogger<FanoutCoordinator>.Instance);

        var blockedResult = await coordinator.RunAsync("billing", null, TestContext.Current.CancellationToken);
        blockedResult.ShouldBe(0);

        await Task.Delay(400, TestContext.Current.CancellationToken);

        var dispatched = await coordinator.RunAsync("billing", null, TestContext.Current.CancellationToken);
        dispatched.ShouldBe(1);

        var messageCount = await CountOutboxMessagesAsync();
        messageCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_RedispatchesAbandonedSlices()
    {
        var leaseFactory = new InMemoryLeaseFactory();
        var slice = new FanoutSlice("analytics", "tenant-7", "hourly");
        var planner = new StaticPlanner(new[] { slice });
        var dispatcher = new FanoutDispatcher(outboxService!);
        var coordinator = new FanoutCoordinator(planner, dispatcher, leaseFactory, NullLogger<FanoutCoordinator>.Instance);

        var firstPass = await coordinator.RunAsync("analytics", null, TestContext.Current.CancellationToken);
        firstPass.ShouldBe(1);

        var secondPass = await coordinator.RunAsync("analytics", null, TestContext.Current.CancellationToken);
        secondPass.ShouldBe(1);

        var count = await CountOutboxMessagesAsync();
        count.ShouldBe(2);

        var payloads = await GetOutboxPayloadsAsync();
        payloads.ShouldAllBe(p => p.Contains("tenant-7", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_SkipsCompletedSlicesAfterCursorAdvances()
    {
        var leaseFactory = new InMemoryLeaseFactory();
        var policyRepo = new SqlFanoutPolicyRepository(Options.Create(fanoutOptions!));
        var cursorRepo = new SqlFanoutCursorRepository(Options.Create(fanoutOptions!));

        await policyRepo.SetCadenceAsync("reports", "daily", everySeconds: 30, jitterSeconds: 0, TestContext.Current.CancellationToken);

        var planner = new ShardedPlanner(policyRepo, cursorRepo, TimeProvider.System, ["shard-a", "shard-b"]);
        var dispatcher = new FanoutDispatcher(outboxService!);
        var coordinator = new FanoutCoordinator(planner, dispatcher, leaseFactory, NullLogger<FanoutCoordinator>.Instance);

        var initialDispatch = await coordinator.RunAsync("reports", null, TestContext.Current.CancellationToken);
        initialDispatch.ShouldBe(2);

        var completionTime = DateTimeOffset.UtcNow;
        await Task.WhenAll(
            cursorRepo.MarkCompletedAsync("reports", "daily", "shard-a", completionTime, TestContext.Current.CancellationToken),
            cursorRepo.MarkCompletedAsync("reports", "daily", "shard-b", completionTime, TestContext.Current.CancellationToken));

        var afterCompletion = await coordinator.RunAsync("reports", null, TestContext.Current.CancellationToken);
        afterCompletion.ShouldBe(0);
    }

    [Fact]
    public async Task FanoutSlices_CanJoinDownstreamMessagesIdempotently()
    {
        var leaseFactory = new InMemoryLeaseFactory();
        var dispatcher = new FanoutDispatcher(outboxService!);
        var correlationId = Guid.NewGuid().ToString("N");

        var slices = new[]
        {
            new FanoutSlice("fanout", "a", "work", correlationId: correlationId),
            new FanoutSlice("fanout", "b", "work", correlationId: correlationId),
            new FanoutSlice("fanout", "c", "work", correlationId: correlationId),
        };

        var planner = new StaticPlanner(slices);
        var coordinator = new FanoutCoordinator(planner, dispatcher, leaseFactory, NullLogger<FanoutCoordinator>.Instance);

        var dispatched = await coordinator.RunAsync("fanout", null, TestContext.Current.CancellationToken);
        dispatched.ShouldBe(3);

        var join = await joinStore!.CreateJoinAsync(42, expectedSteps: 3, metadata: "fan-in", TestContext.Current.CancellationToken);

        var outboxMessages = await GetOutboxMessagesAsync();
        foreach (var message in outboxMessages)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, OutboxMessageIdentifier.From(message.Id), TestContext.Current.CancellationToken);
        }

        var unorderedIds = outboxMessages.Select(m => OutboxMessageIdentifier.From(m.Id)).Reverse().ToArray();
        foreach (var id in unorderedIds)
        {
            await joinStore.IncrementCompletedAsync(join.JoinId, id, TestContext.Current.CancellationToken);
        }

        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, TestContext.Current.CancellationToken);
        finalJoin!.CompletedSteps.ShouldBe(3);
        finalJoin.ExpectedSteps.ShouldBe(3);

        // Replaying the last slice should not double-count
        await joinStore.IncrementCompletedAsync(join.JoinId, unorderedIds.Last(), TestContext.Current.CancellationToken);
        var replayed = await joinStore.GetJoinAsync(join.JoinId, TestContext.Current.CancellationToken);
        replayed!.CompletedSteps.ShouldBe(3);

        var correlationIds = outboxMessages.Select(m => m.CorrelationId).Distinct().ToList();
        correlationIds.Count.ShouldBe(1);
    }

    private async Task<int> CountOutboxMessagesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM dbo.Outbox", cancellationToken: TestContext.Current.CancellationToken));
        return count;
    }

    private async Task<IReadOnlyList<string>> GetOutboxPayloadsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var payloads = await connection.QueryAsync<string>(
            new CommandDefinition("SELECT Payload FROM dbo.Outbox ORDER BY CreatedAt", cancellationToken: TestContext.Current.CancellationToken));
        return payloads.ToList();
    }

    private async Task<IReadOnlyList<(Guid Id, string CorrelationId)>> GetOutboxMessagesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var messages = await connection.QueryAsync<(Guid Id, string CorrelationId)>(
            new CommandDefinition("SELECT Id, CorrelationId FROM dbo.Outbox ORDER BY CreatedAt", cancellationToken: TestContext.Current.CancellationToken));
        return messages.ToList();
    }

    private sealed class StaticPlanner : IFanoutPlanner
    {
        private readonly IReadOnlyList<FanoutSlice> slices;

        public StaticPlanner(IEnumerable<FanoutSlice> slices)
        {
            this.slices = slices.ToList();
        }

        public Task<IReadOnlyList<FanoutSlice>> GetDueSlicesAsync(string fanoutTopic, string? workKey, CancellationToken ct)
        {
            return Task.FromResult(slices);
        }
    }

    private sealed class ShardedPlanner : BaseFanoutPlanner
    {
        private readonly IReadOnlyList<string> shards;

        public ShardedPlanner(
            IFanoutPolicyRepository policyRepository,
            IFanoutCursorRepository cursorRepository,
            TimeProvider timeProvider,
            IReadOnlyList<string> shards)
            : base(policyRepository, cursorRepository, timeProvider)
        {
            this.shards = shards;
        }

        protected override async IAsyncEnumerable<(string ShardKey, string WorkKey)> EnumerateCandidatesAsync(
            string fanoutTopic,
            string? workKey,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var wk = workKey ?? "default";
            foreach (var shard in shards)
            {
                yield return (shard, wk);
                await Task.Yield();
            }
        }
    }

    private sealed class InMemoryLeaseFactory : ISystemLeaseFactory
    {
        private readonly ConcurrentDictionary<string, InMemoryLease> leases = new(StringComparer.Ordinal);
        private readonly TimeSpan? overrideDuration;
        private long fencingToken;

        public InMemoryLeaseFactory(TimeSpan? overrideDuration = null)
        {
            this.overrideDuration = overrideDuration;
        }

        public Task<ISystemLease?> AcquireAsync(
            string resourceName,
            TimeSpan duration,
            string? contextJson = null,
            OwnerToken? ownerToken = default,
            CancellationToken cancellationToken = default)
        {
            var effectiveDuration = overrideDuration ?? duration;

            while (leases.TryGetValue(resourceName, out var existing))
            {
                if (existing.IsExpired)
                {
                    leases.TryRemove(resourceName, out _);
                }
                else
                {
                    return Task.FromResult<ISystemLease?>(null);
                }
            }

            var nextToken = Interlocked.Increment(ref fencingToken);
            var lease = new InMemoryLease(resourceName, nextToken, effectiveDuration, ownerToken ?? OwnerToken.GenerateNew(), RemoveLease);
            leases[resourceName] = lease;
            return Task.FromResult<ISystemLease?>(lease);
        }

        private void RemoveLease(string resourceName)
        {
            leases.TryRemove(resourceName, out _);
        }

        private sealed class InMemoryLease : ISystemLease
        {
            private readonly CancellationTokenSource cts;
            private readonly Action<string> onDispose;
            private readonly TimeSpan duration;
            private readonly DateTimeOffset acquired;

            public InMemoryLease(string resourceName, long fencingToken, TimeSpan duration, OwnerToken ownerToken, Action<string> onDispose)
            {
                ResourceName = resourceName;
                FencingToken = fencingToken;
                OwnerToken = ownerToken;
                this.duration = duration;
                this.onDispose = onDispose;
                acquired = DateTimeOffset.UtcNow;
                cts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(duration, CancellationToken.None).ConfigureAwait(false);
                        cts.Cancel();
                        onDispose(resourceName);
                    }
                    catch
                    {
                    }
                });
            }

            public string ResourceName { get; }

            public OwnerToken OwnerToken { get; }

            public long FencingToken { get; private set; }

            public CancellationToken CancellationToken => cts.Token;

            public bool IsExpired => DateTimeOffset.UtcNow - acquired >= duration;

            public void ThrowIfLost()
            {
                if (IsExpired)
                {
                    throw new LostLeaseException(ResourceName, OwnerToken);
                }
            }

            public Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
            {
                if (IsExpired)
                {
                    return Task.FromResult(false);
                }

                FencingToken++;
                return Task.FromResult(true);
            }

            public ValueTask DisposeAsync()
            {
                cts.Cancel();
                cts.Dispose();
                onDispose(ResourceName);
                return ValueTask.CompletedTask;
            }
        }
    }
}
