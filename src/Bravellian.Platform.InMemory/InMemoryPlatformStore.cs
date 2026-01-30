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

internal sealed class InMemoryPlatformStore
{
    public InMemoryPlatformStore(
        string key,
        InMemoryOutboxOptions outboxOptions,
        InMemoryInboxOptions inboxOptions,
        InMemorySchedulerOptions schedulerOptions,
        InMemoryFanoutOptions fanoutOptions,
        TimeProvider timeProvider)
    {
        Key = key;
        OutboxOptions = outboxOptions;
        InboxOptions = inboxOptions;
        SchedulerOptions = schedulerOptions;
        FanoutOptions = fanoutOptions;

        OutboxState = new InMemoryOutboxState(timeProvider);
        InboxState = new InMemoryInboxState(timeProvider);
        SchedulerState = new InMemorySchedulerState(timeProvider);

        OutboxJoinStore = new InMemoryOutboxJoinStore(timeProvider);
        OutboxService = new InMemoryOutboxService(OutboxState, OutboxJoinStore);
        OutboxStore = new InMemoryOutboxStore(OutboxState, OutboxJoinStore, OutboxOptions, timeProvider);

        InboxService = new InMemoryInboxService(InboxState);
        InboxWorkStore = new InMemoryInboxWorkStore(InboxState);

        SchedulerClient = new InMemorySchedulerClient(SchedulerState);
        SchedulerStore = new InMemorySchedulerStore(SchedulerState);

        FanoutPolicyRepository = new InMemoryFanoutPolicyRepository();
        FanoutCursorRepository = new InMemoryFanoutCursorRepository();

        LeaseFactory = new InMemorySystemLeaseFactory(timeProvider);
    }

    public string Key { get; }

    public InMemoryOutboxOptions OutboxOptions { get; }

    public InMemoryInboxOptions InboxOptions { get; }

    public InMemorySchedulerOptions SchedulerOptions { get; }

    public InMemoryFanoutOptions FanoutOptions { get; }

    public InMemoryOutboxState OutboxState { get; }

    public InMemoryInboxState InboxState { get; }

    public InMemorySchedulerState SchedulerState { get; }

    public InMemoryOutboxJoinStore OutboxJoinStore { get; }

    public InMemoryOutboxService OutboxService { get; }

    public InMemoryOutboxStore OutboxStore { get; }

    public InMemoryInboxService InboxService { get; }

    public InMemoryInboxWorkStore InboxWorkStore { get; }

    public InMemorySchedulerClient SchedulerClient { get; }

    public InMemorySchedulerStore SchedulerStore { get; }

    public InMemoryFanoutPolicyRepository FanoutPolicyRepository { get; }

    public InMemoryFanoutCursorRepository FanoutCursorRepository { get; }

    public InMemorySystemLeaseFactory LeaseFactory { get; }
}
