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

using System.Data;
using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;

internal sealed class PostgresGlobalOutbox : IGlobalOutbox
{
    private readonly IOutbox inner;

    public PostgresGlobalOutbox(IOutbox inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, cancellationToken);

    public Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, correlationId, cancellationToken);

    public Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, correlationId, dueTimeUtc, cancellationToken);

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, transaction, cancellationToken);

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, transaction, correlationId, cancellationToken);

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, payload, transaction, correlationId, dueTimeUtc, cancellationToken);

    public Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) =>
        inner.ClaimAsync(ownerToken, leaseSeconds, batchSize, cancellationToken);

    public Task AckAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) =>
        inner.AckAsync(ownerToken, ids, cancellationToken);

    public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) =>
        inner.AbandonAsync(ownerToken, ids, cancellationToken);

    public Task FailAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) =>
        inner.FailAsync(ownerToken, ids, cancellationToken);

    public Task ReapExpiredAsync(CancellationToken cancellationToken) =>
        inner.ReapExpiredAsync(cancellationToken);

    public Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken) =>
        inner.StartJoinAsync(tenantId, expectedSteps, metadata, cancellationToken);

    public Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) =>
        inner.AttachMessageToJoinAsync(joinId, outboxMessageId, cancellationToken);

    public Task ReportStepCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) =>
        inner.ReportStepCompletedAsync(joinId, outboxMessageId, cancellationToken);

    public Task ReportStepFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) =>
        inner.ReportStepFailedAsync(joinId, outboxMessageId, cancellationToken);
}
