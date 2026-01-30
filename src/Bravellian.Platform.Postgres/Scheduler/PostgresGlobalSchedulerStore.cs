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

internal sealed class PostgresGlobalSchedulerStore : IGlobalSchedulerStore
{
    private readonly ISchedulerStore inner;

    public PostgresGlobalSchedulerStore(ISchedulerStore inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<DateTimeOffset?> GetNextEventTimeAsync(CancellationToken cancellationToken = default) =>
        inner.GetNextEventTimeAsync(cancellationToken);

    public Task<int> CreateJobRunsFromDueJobsAsync(ISystemLease lease, CancellationToken cancellationToken = default) =>
        inner.CreateJobRunsFromDueJobsAsync(lease, cancellationToken);

    public Task<IReadOnlyList<(Guid Id, string Topic, string Payload)>> ClaimDueTimersAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        inner.ClaimDueTimersAsync(lease, batchSize, cancellationToken);

    public Task<IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)>> ClaimDueJobRunsAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        inner.ClaimDueJobRunsAsync(lease, batchSize, cancellationToken);

    public Task UpdateSchedulerStateAsync(ISystemLease lease, CancellationToken cancellationToken = default) =>
        inner.UpdateSchedulerStateAsync(lease, cancellationToken);
}
