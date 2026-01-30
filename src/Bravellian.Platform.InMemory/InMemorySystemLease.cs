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

internal sealed class InMemorySystemLease : ISystemLease
{
    private readonly InMemorySystemLeaseFactory factory;
    private readonly InMemorySystemLeaseFactory.LeaseRecord record;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource cts = new();
    private readonly Timer timer;
    private readonly TimeSpan leaseDuration;
    private bool disposed;

    public InMemorySystemLease(
        InMemorySystemLeaseFactory factory,
        InMemorySystemLeaseFactory.LeaseRecord record,
        TimeSpan leaseDuration,
        TimeProvider timeProvider)
    {
        this.factory = factory;
        this.record = record;
        this.leaseDuration = leaseDuration;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        timer = new Timer(_ => EvaluateExpiry(), null, GetDueTime(), Timeout.InfiniteTimeSpan);
    }

    public string ResourceName => record.ResourceName;

    public OwnerToken OwnerToken => record.OwnerToken;

    public long FencingToken => record.FencingToken;

    public CancellationToken CancellationToken => cts.Token;

    public void ThrowIfLost()
    {
        if (IsExpired() || !IsCurrentOwner())
        {
            Cancel();
            throw new LostLeaseException(ResourceName, OwnerToken);
        }
    }

    public Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            return Task.FromResult(false);
        }

        if (factory.TryRenew(record, leaseDuration))
        {
            ResetTimer();
            return Task.FromResult(true);
        }

        Cancel();
        return Task.FromResult(false);
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        factory.Release(record);
        Cancel();
        timer.Dispose();
        return ValueTask.CompletedTask;
    }

    private void EvaluateExpiry()
    {
        if (disposed)
        {
            return;
        }

        if (IsExpired())
        {
            Cancel();
        }
        else
        {
            ResetTimer();
        }
    }

    private bool IsExpired()
    {
        return record.ExpiresAt <= timeProvider.GetUtcNow();
    }

    private bool IsCurrentOwner()
    {
        if (!factory.TryGetCurrent(record.ResourceName, out var current))
        {
            return false;
        }

        return current != null && current.OwnerToken.Equals(record.OwnerToken);
    }

    private void ResetTimer()
    {
        timer.Change(GetDueTime(), Timeout.InfiniteTimeSpan);
    }

    private TimeSpan GetDueTime()
    {
        var remaining = record.ExpiresAt - timeProvider.GetUtcNow();
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private void Cancel()
    {
        if (!cts.IsCancellationRequested)
        {
            cts.Cancel();
        }
    }
}
