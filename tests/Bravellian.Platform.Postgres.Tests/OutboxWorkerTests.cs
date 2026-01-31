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

using Bravellian.Platform;
using Bravellian.Platform.Outbox;
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxWorkerTests : PostgresTestBase
{
    private PostgresOutboxService? outboxService;
    private TestOutboxWorker? worker;
    private string qualifiedTableName = string.Empty;

    public OutboxWorkerTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);

        var options = Options.Create(new PostgresOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Outbox",
        });

        qualifiedTableName = PostgresSqlHelper.Qualify(options.Value.SchemaName, options.Value.TableName);
        outboxService = new PostgresOutboxService(options, new TestLogger<PostgresOutboxService>(TestOutputHelper));
        worker = new TestOutboxWorker(outboxService, new TestLogger<TestOutboxWorker>(TestOutputHelper));
    }

    public override async ValueTask DisposeAsync()
    {
        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>When the worker processes claimed items, then it acknowledges them and marks them Done.</summary>
    /// <intent>Verify the worker processes and acknowledges all claimed items.</intent>
    /// <scenario>Given three ready outbox items and a running worker.</scenario>
    /// <behavior>ProcessedItems contains all ids and their status is Done.</behavior>
    [Fact]
    public async Task Worker_ProcessesClaimedItems_AndAcknowledgesThem()
    {
        var testIds = await CreateTestOutboxItemsAsync(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker!.StartAsync(cts.Token);

        await Task.Delay(1000, cts.Token);
        await worker.StopAsync(cts.Token);

        worker.ProcessedItems.Count.ShouldBe(3);
        worker.ProcessedItems.ShouldBeSubsetOf(testIds);

        await VerifyOutboxStatusAsync(testIds, OutboxStatus.Done);
    }

    /// <summary>When processing fails, then the worker abandons items back to Ready.</summary>
    /// <intent>Verify failures result in abandon rather than ack.</intent>
    /// <scenario>Given a worker configured to fail processing with two ready items.</scenario>
    /// <behavior>Outbox items return to Ready status.</behavior>
    [Fact]
    public async Task Worker_WithProcessingFailure_AbandonsItems()
    {
        var testIds = await CreateTestOutboxItemsAsync(2);
        worker!.ShouldFailProcessing = true;
        worker.ProcessingDelay = TimeSpan.FromMilliseconds(50);
        worker.RunOnce = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.StartAsync(cts.Token);

        await Task.Delay(3000, cts.Token);
        await worker.StopAsync(cts.Token);

        await VerifyOutboxStatusAsync(testIds, OutboxStatus.Ready);
    }

    /// <summary>When items are claimed, then they are marked InProgress.</summary>
    /// <intent>Verify claim operations update item status.</intent>
    /// <scenario>Given two ready outbox items and a claim call.</scenario>
    /// <behavior>Claimed ids match the inserted items and their status is InProgress.</behavior>
    [Fact]
    public async Task Worker_ClaimsItemsCorrectly()
    {
        var testIds = await CreateTestOutboxItemsAsync(2);

        var claimedIds = await outboxService!.ClaimAsync(
            OwnerToken.GenerateNew(),
            30,
            10,
            TestContext.Current.CancellationToken);

        claimedIds.Count.ShouldBe(2);
        claimedIds.ShouldBeSubsetOf(testIds);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.InProgress);
    }

    /// <summary>When AbandonAsync is called manually, then items return to Ready.</summary>
    /// <intent>Verify manual abandon resets item status.</intent>
    /// <scenario>Given claimed items and the matching owner token.</scenario>
    /// <behavior>Item status is Ready after abandon.</behavior>
    [Fact]
    public async Task Manual_AbandonOperation_Works()
    {
        var testIds = await CreateTestOutboxItemsAsync(2);
        var ownerToken = OwnerToken.GenerateNew();

        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);
        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.InProgress);

        await outboxService.AbandonAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.Ready);
    }

    /// <summary>When a lease expires, then another owner can reclaim the item after reaping.</summary>
    /// <intent>Verify expired leases allow reclamation.</intent>
    /// <scenario>Given an item claimed with a 1-second lease, then reaped after expiry.</scenario>
    /// <behavior>A second claim returns the same item id.</behavior>
    [Fact]
    public async Task WorkQueue_LeaseExpiration_AllowsReclaim()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();

        var claimed1 = await outboxService!.ClaimAsync(owner1, 1, 10, TestContext.Current.CancellationToken);
        claimed1.Count.ShouldBe(1);

        await Task.Delay(1500, TestContext.Current.CancellationToken);

        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        var claimed2 = await outboxService.ClaimAsync(owner2, 30, 10, TestContext.Current.CancellationToken);

        claimed2.Count.ShouldBe(1);
        claimed2[0].ShouldBe(claimed1[0]);
    }

    /// <summary>When a claim is reaped, then the new owner token is recorded on reclaim.</summary>
    /// <intent>Verify owner tokens update when items are reclaimed after expiry.</intent>
    /// <scenario>Given an item claimed by owner1, expired, reaped, and reclaimed by owner2.</scenario>
    /// <behavior>The OwnerToken stored for the item matches owner2.</behavior>
    [Fact]
    public async Task WorkQueue_RestartUsesNewOwnerTokenAfterReap()
    {
        await CreateTestOutboxItemsAsync(1);
        var firstOwner = OwnerToken.GenerateNew();
        var secondOwner = OwnerToken.GenerateNew();

        var claimed1 = await outboxService!.ClaimAsync(firstOwner, 1, 1, TestContext.Current.CancellationToken);
        claimed1.ShouldHaveSingleItem();

        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        var claimed2 = await outboxService.ClaimAsync(secondOwner, 30, 1, TestContext.Current.CancellationToken);

        claimed2.ShouldHaveSingleItem();
        await VerifyOwnerTokenAsync(claimed1[0], secondOwner.Value);
        await VerifyOwnerTokenAsync(claimed2[0], secondOwner.Value);
        claimed1[0].ShouldBe(claimed2[0]);
    }

    /// <summary>When AckAsync is called repeatedly, then the operation is idempotent.</summary>
    /// <intent>Verify repeated acknowledgements leave the item done.</intent>
    /// <scenario>Given claimed items and multiple AckAsync calls by the owner.</scenario>
    /// <behavior>Items remain in Done status.</behavior>
    [Fact]
    public async Task WorkQueue_IdempotentOperations_NoErrors()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var ownerToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.Done);
    }

    /// <summary>When a non-owner attempts to modify items, then status remains InProgress.</summary>
    /// <intent>Verify owner token enforcement for acknowledgements.</intent>
    /// <scenario>Given items claimed by owner1 and an ack attempt by owner2.</scenario>
    /// <behavior>Item status stays InProgress.</behavior>
    [Fact]
    public async Task WorkQueue_UnauthorizedOwner_CannotModify()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(owner1, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.AckAsync(owner2, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.InProgress);
    }

    /// <summary>When work-queue operations receive empty id lists, then they complete without error.</summary>
    /// <intent>Verify empty operations are handled gracefully.</intent>
    /// <scenario>Given empty id lists for AckAsync, AbandonAsync, and FailAsync.</scenario>
    /// <behavior>The operations complete without throwing exceptions.</behavior>
    [Fact]
    public async Task WorkQueue_EmptyIdLists_NoErrors()
    {
        var ownerToken = OwnerToken.GenerateNew();
        var emptyIds = new List<OutboxWorkItemIdentifier>();

        await outboxService!.AckAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
        await outboxService.AbandonAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
        await outboxService.FailAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
    }

    /// <summary>When multiple concurrent claims run, then claimed ids are unique.</summary>
    /// <intent>Verify concurrency does not allow duplicate claims.</intent>
    /// <scenario>Given ten ready items and five concurrent claimers.</scenario>
    /// <behavior>All claimed ids are unique and total claimed does not exceed ten.</behavior>
    [Fact]
    public async Task WorkQueue_ConcurrentClaims_NoOverlap()
    {
        await CreateTestOutboxItemsAsync(10);
        var tasks = new List<Task<IReadOnlyList<OutboxWorkItemIdentifier>>>();

        for (int i = 0; i < 5; i++)
        {
            var ownerToken = OwnerToken.GenerateNew();
            tasks.Add(outboxService!.ClaimAsync(ownerToken, 30, 3, TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        var allClaimed = results.SelectMany(r => r).ToList();
        var uniqueClaimed = allClaimed.Distinct().ToList();

        allClaimed.Count.ShouldBe(uniqueClaimed.Count);
        uniqueClaimed.Count.ShouldBeLessThanOrEqualTo(10);
    }

    /// <summary>When the worker is canceled, then it stops promptly.</summary>
    /// <intent>Verify cancellation short-circuits long processing delays.</intent>
    /// <scenario>Given a worker with a long processing delay and a cancellation token after one second.</scenario>
    /// <behavior>StopAsync completes within five seconds.</behavior>
    [Fact]
    public async Task Worker_RespectsCancellationToken()
    {
        await CreateTestOutboxItemsAsync(5);
        worker!.ProcessingDelay = TimeSpan.FromSeconds(10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StartAsync(cts.Token);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(cts.Token);
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private async Task<List<OutboxWorkItemIdentifier>> CreateTestOutboxItemsAsync(int count)
    {
        var ids = new List<OutboxWorkItemIdentifier>();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        for (int i = 0; i < count; i++)
        {
            var id = OutboxWorkItemIdentifier.GenerateNew();
            ids.Add(id);

            await connection.ExecuteAsync(
                $"""
                INSERT INTO {qualifiedTableName}
                ("Id", "Topic", "Payload", "Status", "CreatedAt", "MessageId")
                VALUES (@Id, @Topic, @Payload, @Status, CURRENT_TIMESTAMP, @MessageId)
                """,
                new { Id = id, Topic = "test", Payload = $"payload{i}", Status = OutboxStatus.Ready, MessageId = Guid.NewGuid() }).ConfigureAwait(false);
        }

        return ids;
    }

    private async Task VerifyOutboxStatusAsync(IEnumerable<OutboxWorkItemIdentifier> ids, byte expectedStatus)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (var id in ids)
        {
            var status = await connection.ExecuteScalarAsync<short>(
                $"""
                SELECT "Status"
                FROM {qualifiedTableName}
                WHERE "Id" = @Id
                """,
                new { Id = id.Value }).ConfigureAwait(false);

            ((byte)status).ShouldBe(expectedStatus);
        }
    }

    private async Task VerifyOwnerTokenAsync(OutboxWorkItemIdentifier id, Guid expectedOwner)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var ownerToken = await connection.ExecuteScalarAsync<Guid?>(
            $"""
            SELECT "OwnerToken"
            FROM {qualifiedTableName}
            WHERE "Id" = @Id
            """,
            new { Id = id.Value }).ConfigureAwait(false);
        ownerToken.ShouldBe(expectedOwner);
    }

    private class TestOutboxWorker : BackgroundService
    {
        private readonly IOutbox outbox;
        private readonly ILogger<TestOutboxWorker> logger;
        private readonly OwnerToken ownerToken = OwnerToken.GenerateNew();

        public TestOutboxWorker(IOutbox outbox, ILogger<TestOutboxWorker> logger)
        {
            this.outbox = outbox;
            this.logger = logger;
        }

        public List<OutboxWorkItemIdentifier> ProcessedItems { get; } = new();

        public bool ShouldFailProcessing { get; set; }

        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        public bool RunOnce { get; set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var claimedIds = await outbox.ClaimAsync(ownerToken, 30, 10, stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("Worker claimed {Count} items", claimedIds.Count);

                    if (claimedIds.Count == 0)
                    {
                        if (RunOnce)
                        {
                            break;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var succeededIds = new List<OutboxWorkItemIdentifier>();
                    var failedIds = new List<OutboxWorkItemIdentifier>();

                    foreach (var id in claimedIds)
                    {
                        try
                        {
                            await Task.Delay(ProcessingDelay, stoppingToken).ConfigureAwait(false);

                            if (ShouldFailProcessing)
                            {
                                logger.LogInformation("Simulating failure for item {Id}", id);
                                throw new InvalidOperationException("Simulated processing failure");
                            }

                            ProcessedItems.Add(id);
                            succeededIds.Add(id);
                            logger.LogInformation("Successfully processed item {Id}", id);
                        }
                        catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
                        {
                            logger.LogWarning(ex, "Failed to process outbox item {Id}", id);
                            failedIds.Add(id);
                        }
                    }

                    if (succeededIds.Count > 0)
                    {
                        logger.LogInformation("Acknowledging {Count} successful items", succeededIds.Count);
                        await outbox.AckAsync(ownerToken, succeededIds, stoppingToken).ConfigureAwait(false);
                    }

                    if (failedIds.Count > 0)
                    {
                        logger.LogInformation("Abandoning {Count} failed items", failedIds.Count);
                        await outbox.AbandonAsync(ownerToken, failedIds, stoppingToken).ConfigureAwait(false);
                    }

                    if (RunOnce)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Worker cancelled due to stopping token");
                    break;
                }
                catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
                {
                    logger.LogError(ex, "Error in outbox processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }
        }
    }
}

