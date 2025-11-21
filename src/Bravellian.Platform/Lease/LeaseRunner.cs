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


using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// A lease runner that acquires a lease and automatically renews it using monotonic timing.
/// </summary>
public sealed class LeaseRunner : IAsyncDisposable
{
    /// <summary>
    /// Maximum jitter in milliseconds to add to renewal timing to prevent herd behavior.
    /// </summary>
    private const int MaxJitterMilliseconds = 1000;

    private readonly LeaseApi leaseApi;
    private readonly IMonotonicClock monotonicClock;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly string leaseName;
    private readonly string owner;
    private readonly TimeSpan leaseDuration;
    private readonly double renewPercent;
    private readonly CancellationTokenSource internalCts = new();
    private readonly Timer renewTimer;
    private readonly object lockObject = new();

    private volatile bool isLost;
    private volatile bool isDisposed;
    private DateTime? leaseUntilUtc;
    private double nextRenewMonotonicTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeaseRunner"/> class.
    /// </summary>
    /// <param name="leaseApi">The lease API for database operations.</param>
    /// <param name="monotonicClock">The monotonic clock for timing.</param>
    /// <param name="timeProvider">The time provider for logging and timestamps.</param>
    /// <param name="leaseName">The name of the lease.</param>
    /// <param name="owner">The owner identifier.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="renewPercent">The percentage of lease duration at which to renew (default 0.6 = 60%).</param>
    /// <param name="logger">The logger instance.</param>
    private LeaseRunner(
        LeaseApi leaseApi,
        IMonotonicClock monotonicClock,
        TimeProvider timeProvider,
        string leaseName,
        string owner,
        TimeSpan leaseDuration,
        double renewPercent,
        ILogger logger)
    {
        this.leaseApi = leaseApi ?? throw new ArgumentNullException(nameof(leaseApi));
        this.monotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.leaseName = leaseName ?? throw new ArgumentNullException(nameof(leaseName));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.leaseDuration = leaseDuration;
        this.renewPercent = renewPercent;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Calculate initial renewal time with jitter
        var renewInterval = TimeSpan.FromMilliseconds(leaseDuration.TotalMilliseconds * renewPercent);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, MaxJitterMilliseconds)); // Small jitter to avoid herd behavior
        var initialDelay = renewInterval + jitter;

        // Start the renewal timer
        renewTimer = new Timer(RenewTimerCallback, null, initialDelay, renewInterval);

        this.logger.LogInformation(
            "Lease runner started for '{LeaseName}' with owner '{Owner}', renew at {RenewPercent:P1}",
            leaseName,
            owner,
            renewPercent);
    }

    /// <summary>
    /// Gets the name of the lease.
    /// </summary>
    public string LeaseName => leaseName;

    /// <summary>
    /// Gets the owner identifier.
    /// </summary>
    public string Owner => owner;

    /// <summary>
    /// Gets a value indicating whether the lease has been lost.
    /// </summary>
    public bool IsLost => isLost;

    /// <summary>
    /// Gets a cancellation token that is canceled when the lease is lost or disposed.
    /// </summary>
    public CancellationToken CancellationToken => internalCts.Token;

    /// <summary>
    /// Acquires a lease and returns a lease runner that will automatically renew it.
    /// </summary>
    /// <param name="leaseApi">The lease API for database operations.</param>
    /// <param name="monotonicClock">The monotonic clock for timing.</param>
    /// <param name="timeProvider">The time provider for logging and timestamps.</param>
    /// <param name="leaseName">The name of the lease.</param>
    /// <param name="owner">The owner identifier.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="renewPercent">The percentage of lease duration at which to renew (default 0.6 = 60%).</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A lease runner if the lease was acquired, null if it was already held by another owner.</returns>
    public static async Task<LeaseRunner?> AcquireAsync(
        LeaseApi leaseApi,
        IMonotonicClock monotonicClock,
        TimeProvider timeProvider,
        string leaseName,
        string owner,
        TimeSpan leaseDuration,
        double renewPercent = 0.6,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var leaseSeconds = (int)Math.Ceiling(leaseDuration.TotalSeconds);
        var result = await leaseApi.AcquireAsync(leaseName, owner, leaseSeconds, cancellationToken).ConfigureAwait(false);

        if (!result.acquired)
        {
            logger.LogDebug("Failed to acquire lease '{LeaseName}' for owner '{Owner}'", leaseName, owner);
            return null;
        }

        logger.LogInformation(
            "Acquired lease '{LeaseName}' for owner '{Owner}', expires at {LeaseUntilUtc:yyyy-MM-dd HH:mm:ss.fff} UTC",
            leaseName,
            owner,
            result.leaseUntilUtc);

        var runner = new LeaseRunner(leaseApi, monotonicClock, timeProvider, leaseName, owner, leaseDuration, renewPercent, logger);
        runner.UpdateLeaseExpiry(result.serverUtcNow, result.leaseUntilUtc);
        return runner;
    }

    /// <summary>
    /// Throws <see cref="LostLeaseException"/> if the lease has been lost.
    /// </summary>
    /// <exception cref="LostLeaseException">Thrown when the lease has been lost.</exception>
    public void ThrowIfLost()
    {
        if (isLost)
        {
            throw new LostLeaseException(leaseName, owner);
        }
    }

    /// <summary>
    /// Attempts to renew the lease immediately.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the lease was successfully renewed, false if it was lost.</returns>
    public async Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
    {
        if (isLost || isDisposed)
        {
            return false;
        }

        return await RenewLeaseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        // Stop the renewal timer
        await renewTimer.DisposeAsync().ConfigureAwait(false);

        // Cancel any ongoing operations
        internalCts.Cancel();

        logger.LogInformation("Lease runner disposed for '{LeaseName}' with owner '{Owner}'", leaseName, owner);

        internalCts.Dispose();
    }

    private void UpdateLeaseExpiry(DateTime serverUtcNow, DateTime? leaseUntilUtc)
    {
        lock (lockObject)
        {
            this.leaseUntilUtc = leaseUntilUtc;

            if (leaseUntilUtc.HasValue)
            {
                // Calculate when to next renew based on monotonic time
                var renewIn = TimeSpan.FromMilliseconds(leaseDuration.TotalMilliseconds * renewPercent);
                nextRenewMonotonicTime = monotonicClock.Seconds + renewIn.TotalSeconds;
            }
        }
    }

    private async void RenewTimerCallback(object? state)
    {
        if (isLost || isDisposed)
        {
            return;
        }

        try
        {
            // Check if it's time to renew based on monotonic time
            var currentMonotonicTime = monotonicClock.Seconds;
            if (currentMonotonicTime < nextRenewMonotonicTime)
            {
                // Not time yet, skip this tick
                return;
            }

            var renewed = await RenewLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (!renewed)
            {
                MarkAsLost();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error during lease renewal for '{LeaseName}' with owner '{Owner}'",
                leaseName,
                owner);

            // Consider the lease lost on renewal errors
            MarkAsLost();
        }
    }

    private async Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        var leaseSeconds = (int)Math.Ceiling(leaseDuration.TotalSeconds);
        var result = await leaseApi.RenewAsync(leaseName, owner, leaseSeconds, cancellationToken).ConfigureAwait(false);

        if (result.renewed)
        {
            UpdateLeaseExpiry(result.serverUtcNow, result.leaseUntilUtc);
            logger.LogDebug(
                "Renewed lease '{LeaseName}' for owner '{Owner}', expires at {LeaseUntilUtc:yyyy-MM-dd HH:mm:ss.fff} UTC",
                leaseName,
                owner,
                result.leaseUntilUtc);
            return true;
        }
        else
        {
            logger.LogWarning(
                "Failed to renew lease '{LeaseName}' for owner '{Owner}' - lease may have expired",
                leaseName,
                owner);
            return false;
        }
    }

    private void MarkAsLost()
    {
        if (!isLost)
        {
            isLost = true;
            internalCts.Cancel();
            logger.LogWarning("Lease '{LeaseName}' for owner '{Owner}' has been lost", leaseName, owner);
        }
    }
}
