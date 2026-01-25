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
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bravellian.Platform;
/// <summary>
/// PostgreSQL-based implementation of a distributed system lease.
/// </summary>
internal sealed class PostgresLease : ISystemLease
{
    private const int GateRetryDelayMs = 50;
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
    private readonly Lock lockObject = new();
    private readonly string lockTable;

    private volatile bool isLost;
    private volatile bool isDisposed;

    private PostgresLease(
        string connectionString,
        string schemaName,
        string resourceName,
        OwnerToken ownerToken,
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
        ResourceName = resourceName;
        OwnerToken = ownerToken;
        FencingToken = fencingToken;
        this.leaseDuration = leaseDuration;
        renewAhead = TimeSpan.FromMilliseconds(leaseDuration.TotalMilliseconds * renewPercent);
        this.useGate = useGate;
        this.gateTimeoutMs = gateTimeoutMs;
        this.logger = logger;
        lockTable = PostgresSqlHelper.Qualify(schemaName, "DistributedLock");

        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(internalCts.Token, userCancellationToken);

        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        var renewInterval = renewAhead + jitter;

        renewTimer = new Timer(RenewTimerCallback, null, renewInterval, renewInterval);

        this.logger.LogInformation(
            "Acquired lease for resource '{ResourceName}' with owner token '{OwnerToken}' and fencing token {FencingToken}",
            resourceName, ownerToken, fencingToken);
    }

    public string ResourceName { get; }

    public OwnerToken OwnerToken { get; }

    public long FencingToken { get; private set; }

    public CancellationToken CancellationToken => linkedCts.Token;

    public static async Task<PostgresLease?> AcquireAsync(
        string connectionString,
        string schemaName,
        string resourceName,
        TimeSpan leaseDuration,
        double renewPercent,
        bool useGate,
        int gateTimeoutMs,
        string? contextJson,
        OwnerToken? ownerToken,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        var token = ownerToken ?? OwnerToken.GenerateNew();
        var leaseSeconds = (int)Math.Ceiling(leaseDuration.TotalSeconds);
        var qualifiedLockTable = PostgresSqlHelper.Qualify(schemaName, "DistributedLock");

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (useGate)
            {
                var acquiredGate = await TryAcquireGateAsync(connection, transaction, resourceName, gateTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!acquiredGate)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }
            }

            var now = await connection.ExecuteScalarAsync<DateTime>(
                "SELECT CURRENT_TIMESTAMP;",
                transaction).ConfigureAwait(false);
            var nowOffset = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
            var newLease = nowOffset.AddSeconds(leaseSeconds);

            await connection.ExecuteAsync(
                $"""
                INSERT INTO {qualifiedLockTable} ("ResourceName", "OwnerToken", "LeaseUntil", "ContextJson")
                VALUES (@ResourceName, NULL, NULL, NULL)
                ON CONFLICT ("ResourceName") DO NOTHING;
                """,
                new { ResourceName = resourceName },
                transaction).ConfigureAwait(false);

            var fencingToken = await connection.ExecuteScalarAsync<long?>(
                $"""
                UPDATE {qualifiedLockTable}
                SET "OwnerToken" = @OwnerToken,
                    "LeaseUntil" = @LeaseUntil,
                    "ContextJson" = @ContextJson,
                    "FencingToken" = "FencingToken" + 1
                WHERE "ResourceName" = @ResourceName
                    AND ("OwnerToken" IS NULL
                        OR "LeaseUntil" IS NULL
                        OR "LeaseUntil" <= @Now
                        OR "OwnerToken" = @OwnerToken)
                RETURNING "FencingToken";
                """,
                new
                {
                    ResourceName = resourceName,
                    OwnerToken = token.Value,
                    LeaseUntil = newLease,
                    ContextJson = contextJson,
                    Now = nowOffset,
                },
                transaction).ConfigureAwait(false);

            if (!fencingToken.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                logger.LogDebug(
                    "Failed to acquire lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
                    resourceName, token);
                return null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new PostgresLease(
                connectionString,
                schemaName,
                resourceName,
                token,
                fencingToken.Value,
                leaseDuration,
                renewPercent,
                useGate,
                gateTimeoutMs,
                cancellationToken,
                logger);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public void ThrowIfLost()
    {
        if (isLost)
        {
            throw new LostLeaseException(ResourceName, OwnerToken);
        }
    }

    public async Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
    {
        if (isLost || isDisposed)
        {
            return false;
        }

        return await RenewLeaseAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        await renewTimer.DisposeAsync().ConfigureAwait(false);
        internalCts.Cancel();

        if (!isLost)
        {
            try
            {
                await ReleaseLeaseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
                    ResourceName, OwnerToken);
            }
        }

        linkedCts.Dispose();
        internalCts.Dispose();

        logger.LogInformation(
            "Disposed lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
            ResourceName, OwnerToken);
    }

    private async void RenewTimerCallback(object? state)
    {
        if (isLost || isDisposed)
        {
            return;
        }

        try
        {
            var renewed = await RenewLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (!renewed)
            {
                MarkAsLost();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during lease renewal for resource '{ResourceName}' with owner token '{OwnerToken}'",
                ResourceName, OwnerToken);
            MarkAsLost();
        }
    }

    private async Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var leaseSeconds = (int)Math.Ceiling(leaseDuration.TotalSeconds);
            var qualifiedLockTable = PostgresSqlHelper.Qualify(schemaName, "DistributedLock");

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var now = await connection.ExecuteScalarAsync<DateTime>("SELECT CURRENT_TIMESTAMP;").ConfigureAwait(false);
            var nowOffset = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
            var newLease = nowOffset.AddSeconds(leaseSeconds);

            var fencingToken = await connection.ExecuteScalarAsync<long?>(
                $"""
                UPDATE {qualifiedLockTable}
                SET "LeaseUntil" = @LeaseUntil,
                    "FencingToken" = "FencingToken" + 1
                WHERE "ResourceName" = @ResourceName
                    AND "OwnerToken" = @OwnerToken
                    AND "LeaseUntil" > @Now
                RETURNING "FencingToken";
                """,
                new
                {
                    ResourceName,
                    OwnerToken = OwnerToken.Value,
                    LeaseUntil = newLease,
                    Now = nowOffset,
                }).ConfigureAwait(false);

            if (fencingToken.HasValue)
            {
                lock (lockObject)
                {
                    FencingToken = fencingToken.Value;
                }

                logger.LogDebug(
                    "Renewed lease for resource '{ResourceName}' with owner token '{OwnerToken}' and fencing token {FencingToken}",
                    ResourceName, OwnerToken, FencingToken);
                return true;
            }

            logger.LogWarning(
                "Failed to renew lease for resource '{ResourceName}' with owner token '{OwnerToken}' - lease may have expired",
                ResourceName, OwnerToken);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.LeaseRenewDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task ReleaseLeaseAsync()
    {
        var qualifiedLockTable = PostgresSqlHelper.Qualify(schemaName, "DistributedLock");

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await connection.ExecuteAsync(
            $"""
            UPDATE {qualifiedLockTable}
            SET "OwnerToken" = NULL,
                "LeaseUntil" = NULL,
                "ContextJson" = NULL
            WHERE "ResourceName" = @ResourceName
                AND "OwnerToken" = @OwnerToken;
            """,
            new { ResourceName, OwnerToken = OwnerToken.Value }).ConfigureAwait(false);

        logger.LogDebug(
            "Released lease for resource '{ResourceName}' with owner token '{OwnerToken}'",
            ResourceName, OwnerToken);
    }

    private void MarkAsLost()
    {
        if (isLost)
        {
            return;
        }

        isLost = true;
        internalCts.Cancel();

        logger.LogWarning(
            "Lease lost for resource '{ResourceName}' with owner token '{OwnerToken}'",
            ResourceName, OwnerToken);
    }

    private static async Task<bool> TryAcquireGateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string resourceName,
        int gateTimeoutMs,
        CancellationToken cancellationToken)
    {
        var lockKey = ComputeAdvisoryLockKey(resourceName);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < gateTimeoutMs)
        {
            var acquired = await connection.ExecuteScalarAsync<bool>(
                "SELECT pg_try_advisory_xact_lock(@Key);",
                new { Key = lockKey },
                transaction).ConfigureAwait(false);

            if (acquired)
            {
                return true;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(GateRetryDelayMs, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static long ComputeAdvisoryLockKey(string resourceName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(resourceName));
        return BitConverter.ToInt64(bytes, 0);
    }
}
