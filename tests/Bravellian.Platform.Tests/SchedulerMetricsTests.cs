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

namespace Bravellian.Platform.Tests;

public sealed class SchedulerMetricsTests
{
    /// <summary>When SchedulerMetrics is referenced, then all work-queue metric definitions are initialized.</summary>
    /// <intent>Verify scheduler work-queue metrics are registered and available.</intent>
    /// <scenario>Given access to the SchedulerMetrics static properties.</scenario>
    /// <behavior>Then all inbox/outbox and timing metrics are non-null.</behavior>
    [Fact]
    public void WorkQueueMetrics_Should_Be_Registered()
    {
        SchedulerMetrics.InboxItemsClaimed.ShouldNotBeNull();
        SchedulerMetrics.InboxItemsAcknowledged.ShouldNotBeNull();
        SchedulerMetrics.InboxItemsAbandoned.ShouldNotBeNull();
        SchedulerMetrics.InboxItemsFailed.ShouldNotBeNull();
        SchedulerMetrics.InboxItemsReaped.ShouldNotBeNull();

        SchedulerMetrics.OutboxItemsClaimed.ShouldNotBeNull();
        SchedulerMetrics.OutboxItemsAcknowledged.ShouldNotBeNull();
        SchedulerMetrics.OutboxItemsAbandoned.ShouldNotBeNull();
        SchedulerMetrics.OutboxItemsFailed.ShouldNotBeNull();
        SchedulerMetrics.OutboxItemsReaped.ShouldNotBeNull();

        SchedulerMetrics.WorkQueueClaimDuration.ShouldNotBeNull();
        SchedulerMetrics.WorkQueueAckDuration.ShouldNotBeNull();
        SchedulerMetrics.WorkQueueAbandonDuration.ShouldNotBeNull();
        SchedulerMetrics.WorkQueueFailDuration.ShouldNotBeNull();
        SchedulerMetrics.WorkQueueReapDuration.ShouldNotBeNull();
        SchedulerMetrics.WorkQueueBatchSize.ShouldNotBeNull();
    }
}

