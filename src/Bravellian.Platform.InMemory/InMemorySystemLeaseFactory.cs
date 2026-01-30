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

internal sealed class InMemorySystemLeaseFactory : ISystemLeaseFactory
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, LeaseRecord> leases = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    public InMemorySystemLeaseFactory(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ISystemLease?> AcquireAsync(
        string resourceName,
        TimeSpan leaseDuration,
        string? contextJson = null,
        OwnerToken? ownerToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Resource name is required.", nameof(resourceName));
        }

        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (leases.TryGetValue(resourceName, out var existing)
                && existing.ExpiresAt > now)
            {
                return Task.FromResult<ISystemLease?>(null);
            }

            var token = ownerToken ?? OwnerToken.GenerateNew();
            var fencingToken = (existing?.FencingToken ?? 0) + 1;
            var record = new LeaseRecord(resourceName, token, now.Add(leaseDuration), fencingToken, contextJson);
            leases[resourceName] = record;

            var lease = new InMemorySystemLease(this, record, leaseDuration, timeProvider);
            return Task.FromResult<ISystemLease?>(lease);
        }
    }

    internal bool TryRenew(LeaseRecord record, TimeSpan leaseDuration)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (!leases.TryGetValue(record.ResourceName, out var existing))
            {
                return false;
            }

            if (!existing.OwnerToken.Equals(record.OwnerToken))
            {
                return false;
            }

            if (existing.ExpiresAt <= now)
            {
                leases.Remove(record.ResourceName);
                return false;
            }

            var updated = existing with
            {
                ExpiresAt = now.Add(leaseDuration),
                FencingToken = existing.FencingToken + 1,
            };

            leases[record.ResourceName] = updated;
            record.Update(updated);
            return true;
        }
    }

    internal bool TryGetCurrent(string resourceName, out LeaseRecord? record)
    {
        lock (sync)
        {
            if (leases.TryGetValue(resourceName, out var current))
            {
                record = current;
                return true;
            }

            record = null;
            return false;
        }
    }

    internal void Release(LeaseRecord record)
    {
        lock (sync)
        {
            if (leases.TryGetValue(record.ResourceName, out var existing)
                && existing.OwnerToken.Equals(record.OwnerToken))
            {
                leases.Remove(record.ResourceName);
            }
        }
    }

    internal sealed record class LeaseRecord
    {
        public LeaseRecord(string resourceName, OwnerToken ownerToken, DateTimeOffset expiresAt, long fencingToken, string? contextJson)
        {
            ResourceName = resourceName;
            OwnerToken = ownerToken;
            ExpiresAt = expiresAt;
            FencingToken = fencingToken;
            ContextJson = contextJson;
        }

        public string ResourceName { get; set; }

        public OwnerToken OwnerToken { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public long FencingToken { get; set; }

        public string? ContextJson { get; set; }

        public void Update(LeaseRecord updated)
        {
            ResourceName = updated.ResourceName;
            OwnerToken = updated.OwnerToken;
            ExpiresAt = updated.ExpiresAt;
            FencingToken = updated.FencingToken;
            ContextJson = updated.ContextJson;
        }
    }
}
