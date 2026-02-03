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

namespace Bravellian.Platform.Operations;

/// <summary>
/// Represents an append-only event emitted by an operation.
/// </summary>
public sealed record OperationEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationEvent"/> record.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="occurredAtUtc">Timestamp when the event occurred (UTC).</param>
    /// <param name="kind">Event kind.</param>
    /// <param name="message">Event message.</param>
    /// <param name="dataJson">Optional JSON payload.</param>
    public OperationEvent(
        OperationId operationId,
        DateTimeOffset occurredAtUtc,
        string kind,
        string message,
        string? dataJson = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Event kind is required.", nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Event message is required.", nameof(message));
        }

        OperationId = operationId;
        OccurredAtUtc = occurredAtUtc;
        Kind = kind.Trim();
        Message = message.Trim();
        DataJson = dataJson;
    }

    /// <summary>
    /// Gets the operation identifier.
    /// </summary>
    public OperationId OperationId { get; }

    /// <summary>
    /// Gets the timestamp when the event occurred (UTC).
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the event message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional JSON payload.
    /// </summary>
    public string? DataJson { get; }
}
