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
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

/// <summary>
/// SQL Server-based implementation of a distributed system lease.
/// </summary>
internal sealed class SqlLease : ISystemLease
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly TimeSpan leaseDuration;
    private readonly TimeSpan renewAhead;
    private readonly bool useGate;
    private readonly int gateTimeoutMs;
    private readonly ILogger logger;
    private readonly CancellationTokenSource internalCts = new();
    private readonly CancellationTokenSource linkedCts;
    private readonly Timer renewTimer;
    private readonly object lockObject = new();

    private volatile bool isLost;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlLease"/> class.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name for the lock table.</param>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="ownerToken">The owner token.</param>
    /// <param name="fencingToken">The initial fencing token.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="renewPercent">The percentage of lease duration at which to renew.</param>
    /// <param name="useGate">Whether to use sp_getapplock gate.</param>
    /// <param name="gateTimeoutMs">The gate timeout in milliseconds.</param>
    /// <param name="userCancellationToken">User cancellation token to link to.</param>
    /// <param name="logger">Logger instance.</param>
    private SqlLease(
        string connectionString,
        string schemaName,
        string resourceName,
        Guid ownerToken,
        long fencingToken,
        TimeSpan leaseDuration,
        double renewPercent,
        bool useGate,
        int gateTimeoutMs,
        CancellationToken userCancellationToken,
        ILogger logger)
    {
        this.connectionString = connectionString;
        this.schemaName = schemaName;
        this.ResourceName = resourceName;
        this.OwnerToken = ownerToken;
        this.FencingToken = fencingToken;
        this.leaseDuration = leaseDuration;
        this.renewAhead = TimeSpan.FromMilliseconds(leaseDuration.TotalMilliseconds * renewPercent);
        this.useGate = useGate;
        this.gateTimeoutMs = gateTimeoutMs;
        this.logger = logger;

        this.linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.internalCts.Token, userCancellationToken);

        // Calculate renewal interval with small jitter
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        var renewInterval = this.renewAhead + jitter;

        // Start the renewal timer
        this.renewTimer = new Timer(this.RenewTimerCallback, null, renewInterval, renewInterval);

        this.logger.LogInformation(
            "Acquired lease for resource '{ResourceName}' with owner token '{OwnerToken}' and fencing token {FencingToken}",
            resourceName, ownerToken, fencingToken);
    }

    /// <inheritdoc/>
    public string ResourceName { get; }

    /// <inheritdoc/>
    public Guid OwnerToken { get; }

    /// <inheritdoc/>
    public long FencingToken { get; private set; }

    /// <inheritdoc/>
    public CancellationToken CancellationToken => this.linkedCts.Token;

    /// <summary>
    /// Attempts to acquire a lease for the specified resource.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name for the lock table.</param>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="renewPercent">The percentage of lease duration at which to renew.</param>
    /// <param name="useGate">Whether to use sp_getapplock gate.</param>
    /// <param name="gateTimeoutMs">The gate timeout in milliseconds.</param>
    /// <param name="contextJson">Optional context JSON.</param>
    /// <param name="ownerToken">Optional owner token; if not provided, a new one will be generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="logger">Logger instance.</param>
    /// <returns>A lease if acquired successfully, null if the resource is already leased.</returns>
    public static async Task<SqlLease?> AcquireAsync(
        string connectionString,
        string schemaName,
        string resourceName,
        TimeSpan leaseDuration,
        double renewPercent,
        bool useGate,
        int gateTimeoutMs,
        string? contextJson,
        Guid? ownerToken,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        var token = ownerToken ?? Guid.NewGuid();
        var leaseSeconds = (int)Math.Ceiling(leaseDuration.TotalSeconds);

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = new SqlCommand($"[{schemaName}].[Lock_Acquire]", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.AddWithValue("@ResourceName", resourceName);
        command.Parameters.AddWithValue("@OwnerToken", token);
        command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
        command.Parameters.AddWithValue("@ContextJson", contextJson ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@UseGate", useGate);
        command.Parameters.AddWithValue("@GateTimeoutMs", gateTimeoutMs);

        var acquiredParam = new SqlParameter("@Acquired", SqlDbType.Bit) { Direction = ParameterDirection.Output };
        var fencingTokenParam = new SqlParameter("@FencingToken", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        command.Parameters.Add(acquiredParam);
        command.Parameters.Add(fencingTokenParam);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var acquired = (bool)acquiredParam.Value;
        if (!acquired)
        {
            logger.LogDebug(
                "Failed to acquire lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
                resourceName, token);
            return null;
        }

        var fencingToken = (long)fencingTokenParam.Value;
        return new SqlLease(
            connectionString,
            schemaName,
            resourceName,
            token,
            fencingToken,
            leaseDuration,
            renewPercent,
            useGate,
            gateTimeoutMs,
            cancellationToken,
            logger);
    }

    /// <inheritdoc/>
    public void ThrowIfLost()
    {
        if (this.isLost)
        {
            throw new LostLeaseException(this.ResourceName, this.OwnerToken);
        }
    }

    /// <inheritdoc/>
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

        // Cancel internal operations
        this.internalCts.Cancel();

        // Try to release the lease (best effort)
        if (!this.isLost)
        {
            try
            {
                await this.ReleaseLeaseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to release lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
                    this.ResourceName, this.OwnerToken);
            }
        }

        this.linkedCts.Dispose();
        this.internalCts.Dispose();

        this.logger.LogInformation(
            "Disposed lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
            this.ResourceName, this.OwnerToken);
    }

    private async void RenewTimerCallback(object? state)
    {
        if (this.isLost || this.isDisposed)
        {
            return;
        }

        try
        {
            var renewed = await this.RenewLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (!renewed)
            {
                this.MarkAsLost();
            }
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Error during lease renewal for resource '{ResourceName}' with owner token '{OwnerToken}'",
                this.ResourceName, this.OwnerToken);

            // Consider the lease lost on renewal errors
            this.MarkAsLost();
        }
    }

    private async Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var leaseSeconds = (int)Math.Ceiling(this.leaseDuration.TotalSeconds);

            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand($"[{this.schemaName}].[Lock_Renew]", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            command.Parameters.AddWithValue("@ResourceName", this.ResourceName);
            command.Parameters.AddWithValue("@OwnerToken", this.OwnerToken);
            command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);

            var renewedParam = new SqlParameter("@Renewed", SqlDbType.Bit) { Direction = ParameterDirection.Output };
            var fencingTokenParam = new SqlParameter("@FencingToken", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            command.Parameters.Add(renewedParam);
            command.Parameters.Add(fencingTokenParam);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var renewed = (bool)renewedParam.Value;
            if (renewed)
            {
                lock (this.lockObject)
                {
                    this.FencingToken = (long)fencingTokenParam.Value;
                }

                this.logger.LogDebug(
                    "Renewed lease for resource '{ResourceName}' with owner token '{OwnerToken}' and fencing token {FencingToken}",
                    this.ResourceName, this.OwnerToken, this.FencingToken);
                return true;
            }
            else
            {
                this.logger.LogWarning(
                    "Failed to renew lease for resource '{ResourceName}' with owner token '{OwnerToken}' - lease may have expired",
                    this.ResourceName, this.OwnerToken);
                return false;
            }
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.LeaseRenewDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task ReleaseLeaseAsync()
    {
        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using var command = new SqlCommand($"[{this.schemaName}].[Lock_Release]", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.AddWithValue("@ResourceName", this.ResourceName);
        command.Parameters.AddWithValue("@OwnerToken", this.OwnerToken);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        this.logger.LogDebug(
            "Released lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
            this.ResourceName, this.OwnerToken);
    }

    private void MarkAsLost()
    {
        if (this.isLost)
        {
            return;
        }

        this.isLost = true;
        this.internalCts.Cancel();

        this.logger.LogWarning(
            "Lease lost for resource '{ResourceName}' with owner token '{OwnerToken}'",
            this.ResourceName, this.OwnerToken);
    }
}
