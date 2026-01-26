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

/// <summary>
/// Represents the persisted state of an external side effect.
/// </summary>
public sealed record ExternalSideEffectRecord
{
    /// <summary>
    /// Gets the record identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the operation name.
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// Gets the idempotency key.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Gets the current status.
    /// </summary>
    public ExternalSideEffectStatus Status { get; init; }

    /// <summary>
    /// Gets the number of execution attempts.
    /// </summary>
    public int AttemptCount { get; init; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the last updated timestamp.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Gets the last attempt timestamp.
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; init; }

    /// <summary>
    /// Gets the last external check timestamp.
    /// </summary>
    public DateTimeOffset? LastExternalCheckAt { get; init; }

    /// <summary>
    /// Gets the lock expiration timestamp.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; init; }

    /// <summary>
    /// Gets the identifier of the locker.
    /// </summary>
    public Guid? LockedBy { get; init; }

    /// <summary>
    /// Gets the correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid? OutboxMessageId { get; init; }

    /// <summary>
    /// Gets the external reference identifier.
    /// </summary>
    public string? ExternalReferenceId { get; init; }

    /// <summary>
    /// Gets the external status value.
    /// </summary>
    public string? ExternalStatus { get; init; }

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the payload hash used for idempotency.
    /// </summary>
    public string? PayloadHash { get; init; }
}
