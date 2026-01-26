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


using System.Diagnostics;
using System.Reflection;
using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class LeaseRunnerTests : SqlServerTestBase
{
    private LeaseApi? leaseApi;

    public LeaseRunnerTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure the lease schema exists
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        leaseApi = new LeaseApi(ConnectionString, "infra");
    }

    /// <summary>
    /// When a lease is available, then AcquireAsync returns a runner with initialized state.
    /// </summary>
    /// <intent>
    /// Validate LeaseRunner acquisition returns a usable runner instance.
    /// </intent>
    /// <scenario>
    /// Given a unique lease name, owner, and lease duration using the real LeaseApi.
    /// </scenario>
    /// <behavior>
    /// Then the runner is not null, has expected name/owner, and is not marked lost.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithAvailableLease_ReturnsRunnerInstance()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;

        // Act
        var runner = await LeaseRunner.AcquireAsync(leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            logger: logger, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        runner.ShouldNotBeNull();
        runner.LeaseName.ShouldBe(leaseName);
        runner.Owner.ShouldBe(owner);
        runner.IsLost.ShouldBeFalse();
        runner.CancellationToken.IsCancellationRequested.ShouldBeFalse();

        // Cleanup
        await runner.DisposeAsync();
    }

    /// <summary>
    /// When a lease is already held, then a second AcquireAsync returns null.
    /// </summary>
    /// <intent>
    /// Ensure LeaseRunner does not acquire a lease already owned by another runner.
    /// </intent>
    /// <scenario>
    /// Given owner1 acquires a lease and owner2 attempts to acquire the same lease immediately after.
    /// </scenario>
    /// <behavior>
    /// Then the second acquisition returns null.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithUnavailableLease_ReturnsNull()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner1 = "owner1";
        var owner2 = "owner2";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;
        var api = leaseApi ?? throw new InvalidOperationException("Lease API has not been initialized.");

        // First runner acquires the lease
        var runner1 = await LeaseRunner.AcquireAsync(
            api,
            monotonicClock,
            timeProvider,
            leaseName,
            owner1,
            leaseDuration,
            logger: logger,
            cancellationToken: TestContext.Current.CancellationToken);

        runner1.ShouldNotBeNull();

        // Act - Second runner tries to acquire the same lease
        var runner2 = await LeaseRunner.AcquireAsync(
            api,
            monotonicClock,
            timeProvider,
            leaseName,
            owner2,
            leaseDuration,
            logger: logger,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        runner2.ShouldBeNull();

        // Cleanup
        await runner1.DisposeAsync();
    }

    /// <summary>
    /// When TryRenewNowAsync is called on a valid runner, then renewal succeeds and the lease is not lost.
    /// </summary>
    /// <intent>
    /// Confirm manual renewal succeeds for a current lease owner.
    /// </intent>
    /// <scenario>
    /// Given a LeaseRunner acquired with a valid lease and owner.
    /// </scenario>
    /// <behavior>
    /// Then TryRenewNowAsync returns true and IsLost remains false.
    /// </behavior>
    [Fact]
    public async Task TryRenewNowAsync_WithValidRunner_SucceedsAndExtendsLease()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;

        var runner = await LeaseRunner.AcquireAsync(leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            logger: logger, cancellationToken: TestContext.Current.CancellationToken);

        runner.ShouldNotBeNull();

        // Act
        var renewed = await runner.TryRenewNowAsync(TestContext.Current.CancellationToken);

        // Assert
        renewed.ShouldBeTrue();
        runner.IsLost.ShouldBeFalse();

        // Cleanup
        await runner.DisposeAsync();
    }

    /// <summary>
    /// When a lease is still valid, then ThrowIfLost does not throw.
    /// </summary>
    /// <intent>
    /// Ensure the guard does not falsely signal a lost lease.
    /// </intent>
    /// <scenario>
    /// Given a LeaseRunner acquired with a valid lease and no renewal failures.
    /// </scenario>
    /// <behavior>
    /// Then calling ThrowIfLost completes without exception.
    /// </behavior>
    [Fact]
    public async Task ThrowIfLost_WhenLeaseIsValid_DoesNotThrow()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;

        var runner = await LeaseRunner.AcquireAsync(leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            logger: logger, cancellationToken: TestContext.Current.CancellationToken);

        runner.ShouldNotBeNull();

        // Act & Assert
        Should.NotThrow(() => runner.ThrowIfLost());

        // Cleanup
        await runner.DisposeAsync();
    }

    /// <summary>
    /// When a custom renew percent is used, then automatic renewal keeps the runner valid past the threshold.
    /// </summary>
    /// <intent>
    /// Validate that automatic renewal triggers early enough to avoid lease loss.
    /// </intent>
    /// <scenario>
    /// Given a short lease duration with a 50% renew percent and a wait past half the duration.
    /// </scenario>
    /// <behavior>
    /// Then the runner remains active and its cancellation token is not canceled.
    /// </behavior>
    [Fact]
    public async Task MonotonicRenewal_WithCustomRenewPercent_RenewsAtCorrectInterval()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseDuration = TimeSpan.FromSeconds(10); // Short lease for testing
        var renewPercent = 0.5; // Renew at 50%
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;

        var runner = await LeaseRunner.AcquireAsync(leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            renewPercent,
            logger: logger, TestContext.Current.CancellationToken);

        runner.ShouldNotBeNull();

        // Act - Wait for potential renewal
        await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken); // Wait past 50% of lease duration

        // Assert - Runner should still be valid (automatic renewals should have occurred)
        runner.IsLost.ShouldBeFalse();
        runner.CancellationToken.IsCancellationRequested.ShouldBeFalse();

        // Cleanup
        await runner.DisposeAsync();
    }

    /// <summary>
    /// When a runner is disposed, then TryRenewNowAsync returns false.
    /// </summary>
    /// <intent>
    /// Ensure disposed runners do not attempt renewals.
    /// </intent>
    /// <scenario>
    /// Given a LeaseRunner that has been disposed after acquisition.
    /// </scenario>
    /// <behavior>
    /// Then TryRenewNowAsync returns false.
    /// </behavior>
    [Fact]
    public async Task DisposedRunner_DoesNotRenew()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var monotonicClock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger.Instance;

        var runner = await LeaseRunner.AcquireAsync(leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            logger: logger, cancellationToken: TestContext.Current.CancellationToken);

        runner.ShouldNotBeNull();

        // Act - Dispose the runner
        await runner.DisposeAsync();

        // Assert - TryRenewNowAsync should return false after disposal
        var renewed = await runner.TryRenewNowAsync(TestContext.Current.CancellationToken);
        renewed.ShouldBeFalse();
    }

    /// <summary>
    /// When the monotonic clock jumps forward, then renewal scheduling advances based on monotonic time without over-renewing.
    /// </summary>
    /// <intent>
    /// Confirm renewal scheduling relies on monotonic time even across simulated pauses.
    /// </intent>
    /// <scenario>
    /// Given a LeaseRunner using a FakeMonotonicClock and reflection to inspect the next renewal timestamp.
    /// </scenario>
    /// <behavior>
    /// Then the scheduled renewal time increases after the pause and remains stable on an immediate retry.
    /// </behavior>
    [Fact]
    public async Task RenewTimer_UsesMonotonicClockAcrossClockSkewAndGcPauses()
    {
        // Arrange
        var leaseName = $"test-runner-{Guid.NewGuid():N}";
        var owner = "monotonic-owner";
        var leaseDuration = TimeSpan.FromSeconds(20);
        var renewPercent = 0.6;
        var monotonicClock = new FakeMonotonicClock(startSeconds: 10_000);
        var timeProvider = TimeProvider.System; // Wall clock skew should not affect scheduling
        var logger = NullLogger.Instance;

        var runner = await LeaseRunner.AcquireAsync(
            leaseApi!,
            monotonicClock,
            timeProvider,
            leaseName,
            owner,
            leaseDuration,
            renewPercent,
            logger: logger,
            cancellationToken: TestContext.Current.CancellationToken);

        runner.ShouldNotBeNull();

        var nextRenewField = typeof(LeaseRunner).GetField("nextRenewMonotonicTime", BindingFlags.NonPublic | BindingFlags.Instance);
        if (nextRenewField == null)
        {
            throw new InvalidOperationException("LeaseRunner.nextRenewMonotonicTime field not found. The internal implementation may have changed.");
        }

        var scheduledBeforeFieldValue = nextRenewField.GetValue(runner);
        if (scheduledBeforeFieldValue == null)
        {
            throw new InvalidOperationException("LeaseRunner.nextRenewMonotonicTime field value is null.");
        }

        var scheduledBeforePause = (double)scheduledBeforeFieldValue;
        scheduledBeforePause.ShouldBeGreaterThan(monotonicClock.Seconds);

        // Act - Simulate a GC pause and a wall clock skew (monotonic clock jumps forward)
        monotonicClock.Advance(TimeSpan.FromSeconds(30));

        var renewTimerCallback = typeof(LeaseRunner)
            .GetMethod("RenewTimerCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (renewTimerCallback == null)
        {
            throw new InvalidOperationException("LeaseRunner.RenewTimerCallback method not found. The internal implementation may have changed.");
        }

        // Invoke the renewal callback manually to validate monotonic scheduling after the pause
        renewTimerCallback.Invoke(runner, new object?[] { null });

        var scheduledAfterFieldValue = nextRenewField.GetValue(runner);
        if (scheduledAfterFieldValue == null)
        {
            throw new InvalidOperationException("LeaseRunner.nextRenewMonotonicTime field value is null after renewal.");
        }

        var scheduledAfterPause = (double)scheduledAfterFieldValue;

        // Assert - Renewal reschedules based on monotonic time, not wall clock
        runner.IsLost.ShouldBeFalse();
        scheduledAfterPause.ShouldBeGreaterThan(monotonicClock.Seconds);
        scheduledAfterPause.ShouldBeGreaterThan(scheduledBeforePause);

        // Invoke again without advancing monotonic clock to verify jitter/periodic ticks do not over-renew
        renewTimerCallback.Invoke(runner, new object?[] { null });
        var scheduledAfterImmediateRetryFieldValue = nextRenewField.GetValue(runner);
        if (scheduledAfterImmediateRetryFieldValue == null)
        {
            throw new InvalidOperationException("LeaseRunner.nextRenewMonotonicTime field value is null after immediate retry.");
        }

        var scheduledAfterImmediateRetry = (double)scheduledAfterImmediateRetryFieldValue;
        scheduledAfterImmediateRetry.ShouldBe(scheduledAfterPause);

        await runner.DisposeAsync();
    }
}
