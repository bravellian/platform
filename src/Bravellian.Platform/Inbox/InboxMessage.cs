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
/// Represents an inbound message for processing through the Inbox Handler system.
/// </summary>
public sealed record InboxMessage
{
    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    public InboxMessageIdentifier MessageId { get; internal init; }

    /// <summary>
    /// Gets the source system for the message.
    /// </summary>
    public string Source { get; internal init; } = string.Empty;

    /// <summary>
    /// Gets the message topic.
    /// </summary>
    public string Topic { get; internal init; } = string.Empty;

    /// <summary>
    /// Gets the message payload.
    /// </summary>
    public string Payload { get; internal init; } = string.Empty;

    /// <summary>
    /// Gets the payload hash when available.
    /// </summary>
    [SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "Hash is stored as raw bytes.")]
    public byte[]? Hash { get; internal init; }

    /// <summary>
    /// Gets the processing attempt count.
    /// </summary>
    public int Attempt { get; internal init; }

    /// <summary>
    /// Gets the processing attempt count (canonical name).
    /// </summary>
    public int AttemptCount => Attempt;

    /// <summary>
    /// Gets the first seen timestamp in UTC.
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; internal init; }

    /// <summary>
    /// Gets the created timestamp (canonical name).
    /// </summary>
    public DateTimeOffset CreatedOn => FirstSeenUtc;

    /// <summary>
    /// Gets the last seen timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastSeenUtc { get; internal init; }

    /// <summary>
    /// Gets the processed timestamp in UTC, when available.
    /// </summary>
    public DateTimeOffset? ProcessedUtc { get; internal init; }

    /// <summary>
    /// Gets the processed timestamp (canonical name).
    /// </summary>
    public DateTimeOffset? ProcessedOn => ProcessedUtc;

    /// <summary>
    /// Gets the due time in UTC, when scheduled.
    /// </summary>
    public DateTimeOffset? DueTimeUtc { get; internal init; }

    /// <summary>
    /// Gets the due time (canonical name).
    /// </summary>
    public DateTimeOffset? DueOn => DueTimeUtc;

    /// <summary>
    /// Gets the correlation identifier, if provided.
    /// </summary>
    public string? CorrelationId { get; internal init; }

    /// <summary>
    /// Gets the processor identifier, if any.
    /// </summary>
    public string? ProcessedBy { get; internal init; }

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    public string? LastError { get; internal init; }
}
