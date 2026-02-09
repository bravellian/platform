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
/// Represents an outbox message awaiting dispatch.
/// </summary>
public sealed record OutboxMessage
{
    /// <summary>
    /// Gets the outbox work item identifier.
    /// </summary>
    public OutboxWorkItemIdentifier Id { get; internal init; }

    /// <summary>
    /// Gets the message payload.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// Gets the message topic.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; internal init; }

    /// <summary>
    /// Gets the creation timestamp (canonical name).
    /// </summary>
    public DateTimeOffset CreatedOn => CreatedAt;

    /// <summary>
    /// Gets a value indicating whether the message has been processed.
    /// </summary>
    public bool IsProcessed { get; internal init; }

    /// <summary>
    /// Gets the processed timestamp, if any.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; internal init; }

    /// <summary>
    /// Gets the processed timestamp (canonical name).
    /// </summary>
    public DateTimeOffset? ProcessedOn => ProcessedAt;

    /// <summary>
    /// Gets the processor identifier, if any.
    /// </summary>
    public string? ProcessedBy { get; internal init; }

    /// <summary>
    /// Gets the retry count.
    /// </summary>
    public int RetryCount { get; internal init; }

    /// <summary>
    /// Gets the retry count (canonical name).
    /// </summary>
    public int AttemptCount => RetryCount;

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    public string? LastError { get; internal init; }

    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    public OutboxMessageIdentifier MessageId { get; internal init; }

    /// <summary>
    /// Gets the correlation identifier.
    /// </summary>
    public string? CorrelationId { get; internal init; }

    /// <summary>
    /// Gets the due time in UTC, when scheduled.
    /// </summary>
    public DateTimeOffset? DueTimeUtc { get; internal init; }

    /// <summary>
    /// Gets the due time (canonical name).
    /// </summary>
    public DateTimeOffset? DueOn => DueTimeUtc;
}
