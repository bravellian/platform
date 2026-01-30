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

internal sealed class InMemorySchedulerStore : ISchedulerStore
{
    private readonly InMemorySchedulerState state;
    private long currentFencingToken;

    public InMemorySchedulerStore(InMemorySchedulerState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<DateTimeOffset?> GetNextEventTimeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(state.GetNextEventTime());
    }

    public Task<int> CreateJobRunsFromDueJobsAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        lease.ThrowIfLost();
        if (lease.FencingToken < currentFencingToken)
        {
            return Task.FromResult(0);
        }

        var count = state.CreateJobRunsFromDueJobs();
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<(Guid Id, string Topic, string Payload)>> ClaimDueTimersAsync(ISystemLease lease, int batchSize, CancellationToken cancellationToken = default)
    {
        lease.ThrowIfLost();
        if (lease.FencingToken < currentFencingToken)
        {
            return Task.FromResult<IReadOnlyList<(Guid, string, string)>>(Array.Empty<(Guid, string, string)>());
        }

        var ids = state.ClaimTimers(lease.OwnerToken, leaseSeconds: 30, batchSize);
        var result = state.GetClaimedTimers(ids);
        return Task.FromResult<IReadOnlyList<(Guid, string, string)>>(result);
    }

    public Task<IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)>> ClaimDueJobRunsAsync(ISystemLease lease, int batchSize, CancellationToken cancellationToken = default)
    {
        lease.ThrowIfLost();
        if (lease.FencingToken < currentFencingToken)
        {
            return Task.FromResult<IReadOnlyList<(Guid, Guid, string, string)>>(Array.Empty<(Guid, Guid, string, string)>());
        }

        var ids = state.ClaimJobRuns(lease.OwnerToken, leaseSeconds: 30, batchSize);
        var result = state.GetClaimedJobRuns(ids);
        return Task.FromResult<IReadOnlyList<(Guid, Guid, string, string)>>(result);
    }

    public Task UpdateSchedulerStateAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        currentFencingToken = lease.FencingToken;
        return Task.CompletedTask;
    }
}
