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

using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;

/// <summary>
/// Provides data access operations for the outbox store.
/// This is a thin, SQL-backed interface that the dispatcher uses.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Claims due messages for processing with a lease/lock mechanism.
    /// Uses SQL Server hints like WITH (READPAST, UPDLOCK, ROWLOCK) for concurrency.
    /// </summary>
    /// <param name="limit">Maximum number of messages to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of claimed outbox messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Mark as successfully dispatched.</summary>
    /// <param name="id">Message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken);

    /// <summary>Reschedule with backoff and record error.</summary>
    /// <param name="id">Message ID.</param>
    /// <param name="delay">Delay before next attempt.</param>
    /// <param name="lastError">Error message to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken);

    /// <summary>Mark as permanently failed (no further retries).</summary>
    /// <param name="id">Message ID.</param>
    /// <param name="lastError">Error message to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken);
}
