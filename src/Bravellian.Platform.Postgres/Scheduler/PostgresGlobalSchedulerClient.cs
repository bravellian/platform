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

internal sealed class PostgresGlobalSchedulerClient : IGlobalSchedulerClient
{
    private readonly ISchedulerClient inner;

    public PostgresGlobalSchedulerClient(ISchedulerClient inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime, CancellationToken cancellationToken) =>
        inner.ScheduleTimerAsync(topic, payload, dueTime, cancellationToken);

    public Task<bool> CancelTimerAsync(string timerId, CancellationToken cancellationToken) =>
        inner.CancelTimerAsync(timerId, cancellationToken);

    public Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, CancellationToken cancellationToken) =>
        inner.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, cancellationToken);

    public Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload, CancellationToken cancellationToken) =>
        inner.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, payload, cancellationToken);

    public Task DeleteJobAsync(string jobName, CancellationToken cancellationToken) =>
        inner.DeleteJobAsync(jobName, cancellationToken);

    public Task TriggerJobAsync(string jobName, CancellationToken cancellationToken) =>
        inner.TriggerJobAsync(jobName, cancellationToken);

    public Task<IReadOnlyList<Guid>> ClaimTimersAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) =>
        inner.ClaimTimersAsync(ownerToken, leaseSeconds, batchSize, cancellationToken);

    public Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) =>
        inner.ClaimJobRunsAsync(ownerToken, leaseSeconds, batchSize, cancellationToken);

    public Task AckTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        inner.AckTimersAsync(ownerToken, ids, cancellationToken);

    public Task AckJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        inner.AckJobRunsAsync(ownerToken, ids, cancellationToken);

    public Task AbandonTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        inner.AbandonTimersAsync(ownerToken, ids, cancellationToken);

    public Task AbandonJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        inner.AbandonJobRunsAsync(ownerToken, ids, cancellationToken);

    public Task ReapExpiredTimersAsync(CancellationToken cancellationToken) =>
        inner.ReapExpiredTimersAsync(cancellationToken);

    public Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken) =>
        inner.ReapExpiredJobRunsAsync(cancellationToken);
}
