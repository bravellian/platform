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

namespace Bravellian.Platform;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
/// A lease runner that acquires a lease and automatically renews it using monotonic timing.
/// </summary>
public sealed class LeaseRunner : IAsyncDisposable
{
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
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)); // Small jitter to avoid herd behavior
        var initialDelay = renewInterval + jitter;

        // Start the renewal timer
        this.renewTimer = new Timer(this.RenewTimerCallback, null, initialDelay, initialDelay);

        this.logger.LogInformation("Lease runner started for '{LeaseName}' with owner '{Owner}', renew at {RenewPercent:P1}",
            leaseName, owner, renewPercent);
    }

    /// <summary>
    /// Gets the name of the lease.
    /// </summary>
    public string LeaseName => this.leaseName;

    /// <summary>
    /// Gets the owner identifier.
    /// </summary>
    public string Owner => this.owner;

    /// <summary>
    /// Gets a value indicating whether the lease has been lost.
    /// </summary>
    public bool IsLost => this.isLost;

    /// <summary>
    /// Gets a cancellation token that is canceled when the lease is lost or disposed.
    /// </summary>
    public CancellationToken CancellationToken => this.internalCts.Token;

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

        if (!result.Acquired)
        {
            logger.LogDebug("Failed to acquire lease '{LeaseName}' for owner '{Owner}'", leaseName, owner);
            return null;
        }

        logger.LogInformation("Acquired lease '{LeaseName}' for owner '{Owner}', expires at {LeaseUntilUtc:yyyy-MM-dd HH:mm:ss.fff} UTC",
            leaseName, owner, result.LeaseUntilUtc);

        var runner = new LeaseRunner(leaseApi, monotonicClock, timeProvider, leaseName, owner, leaseDuration, renewPercent, logger);
        runner.UpdateLeaseExpiry(result.ServerUtcNow, result.LeaseUntilUtc);
        return runner;
    }

    /// <summary>
    /// Throws <see cref="LostLeaseException"/> if the lease has been lost.
    /// </summary>
    /// <exception cref="LostLeaseException">Thrown when the lease has been lost.</exception>
    public void ThrowIfLost()
    {
        if (this.isLost)
        {
            throw new LostLeaseException(this.leaseName, this.owner);
        }
    }

    /// <summary>
    /// Attempts to renew the lease immediately.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the lease was successfully renewed, false if it was lost.</returns>
    public async Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
    {
        if (this.isLost || this.isDisposed)
        {
            return false;
        }

        return await this.RenewLeaseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;

        // Stop the renewal timer
        await this.renewTimer.DisposeAsync().ConfigureAwait(false);

        // Cancel any ongoing operations
        this.internalCts.Cancel();

        this.logger.LogInformation("Lease runner disposed for '{LeaseName}' with owner '{Owner}'", this.leaseName, this.owner);

        this.internalCts.Dispose();
    }

    private void UpdateLeaseExpiry(DateTime serverUtcNow, DateTime? leaseUntilUtc)
    {
        lock (this.lockObject)
        {
            this.leaseUntilUtc = leaseUntilUtc;
            
            if (leaseUntilUtc.HasValue)
            {
                // Calculate when to next renew based on monotonic time
                var renewIn = TimeSpan.FromMilliseconds(this.leaseDuration.TotalMilliseconds * this.renewPercent);
                this.nextRenewMonotonicTime = this.monotonicClock.Seconds + renewIn.TotalSeconds;
            }
        }
    }

    private async void RenewTimerCallback(object? state)
    {
        if (this.isLost || this.isDisposed)
        {
            return;
        }

        try
        {
            // Check if it's time to renew based on monotonic time
            var currentMonotonicTime = this.monotonicClock.Seconds;
            if (currentMonotonicTime < this.nextRenewMonotonicTime)
            {
                // Not time yet, skip this tick
                return;
            }

            var renewed = await this.RenewLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (!renewed)
            {
                this.MarkAsLost();
            }
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Error during lease renewal for '{LeaseName}' with owner '{Owner}'",
                this.leaseName, this.owner);

            // Consider the lease lost on renewal errors
            this.MarkAsLost();
        }
    }

    private async Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        var leaseSeconds = (int)Math.Ceiling(this.leaseDuration.TotalSeconds);
        var result = await this.leaseApi.RenewAsync(this.leaseName, this.owner, leaseSeconds, cancellationToken).ConfigureAwait(false);

        if (result.Renewed)
        {
            this.UpdateLeaseExpiry(result.ServerUtcNow, result.LeaseUntilUtc);
            this.logger.LogDebug("Renewed lease '{LeaseName}' for owner '{Owner}', expires at {LeaseUntilUtc:yyyy-MM-dd HH:mm:ss.fff} UTC",
                this.leaseName, this.owner, result.LeaseUntilUtc);
            return true;
        }
        else
        {
            this.logger.LogWarning("Failed to renew lease '{LeaseName}' for owner '{Owner}' - lease may have expired",
                this.leaseName, this.owner);
            return false;
        }
    }

    private void MarkAsLost()
    {
        if (!this.isLost)
        {
            this.isLost = true;
            this.internalCts.Cancel();
            this.logger.LogWarning("Lease '{LeaseName}' for owner '{Owner}' has been lost", this.leaseName, this.owner);
        }
    }
}