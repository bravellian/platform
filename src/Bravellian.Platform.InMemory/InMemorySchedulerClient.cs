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

internal sealed class InMemorySchedulerClient : ISchedulerClient
{
    private readonly InMemorySchedulerState state;

    public InMemorySchedulerClient(InMemorySchedulerState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var id = state.ScheduleTimer(topic, payload, dueTime);
        return Task.FromResult(id);
    }

    public Task<bool> CancelTimerAsync(string timerId, CancellationToken cancellationToken)
    {
        var result = state.CancelTimer(timerId);
        return Task.FromResult(result);
    }

    public Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, CancellationToken cancellationToken)
    {
        return CreateOrUpdateJobAsync(jobName, topic, cronSchedule, null, cancellationToken);
    }

    public Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronSchedule);

        state.CreateOrUpdateJob(jobName, topic, cronSchedule, payload);
        return Task.CompletedTask;
    }

    public Task DeleteJobAsync(string jobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        state.DeleteJob(jobName);
        return Task.CompletedTask;
    }

    public Task TriggerJobAsync(string jobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        state.TriggerJob(jobName);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ClaimTimersAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
    {
        var claimed = state.ClaimTimers(ownerToken, leaseSeconds, batchSize);
        return Task.FromResult<IReadOnlyList<Guid>>(claimed);
    }

    public Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
    {
        var claimed = state.ClaimJobRuns(ownerToken, leaseSeconds, batchSize);
        return Task.FromResult<IReadOnlyList<Guid>>(claimed);
    }

    public Task AckTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        state.AckTimers(ownerToken, ids);
        return Task.CompletedTask;
    }

    public Task AckJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        state.AckJobRuns(ownerToken, ids);
        return Task.CompletedTask;
    }

    public Task AbandonTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        state.AbandonTimers(ownerToken, ids);
        return Task.CompletedTask;
    }

    public Task AbandonJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        state.AbandonJobRuns(ownerToken, ids);
        return Task.CompletedTask;
    }

    public Task ReapExpiredTimersAsync(CancellationToken cancellationToken)
    {
        state.ReapExpiredTimers();
        return Task.CompletedTask;
    }

    public Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken)
    {
        state.ReapExpiredJobRuns();
        return Task.CompletedTask;
    }
}
