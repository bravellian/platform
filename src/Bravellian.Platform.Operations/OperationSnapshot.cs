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

using Bravellian.Platform.Correlation;

namespace Bravellian.Platform.Operations;

/// <summary>
/// Represents the current snapshot of a long-running operation.
/// </summary>
public sealed record OperationSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationSnapshot"/> record.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="name">Operation name.</param>
    /// <param name="status">Operation status.</param>
    /// <param name="startedAtUtc">Start timestamp (UTC).</param>
    /// <param name="updatedAtUtc">Last update timestamp (UTC).</param>
    /// <param name="completedAtUtc">Completion timestamp (UTC).</param>
    /// <param name="percentComplete">Percentage complete (0-100).</param>
    /// <param name="message">Progress message.</param>
    /// <param name="correlation">Optional correlation context.</param>
    /// <param name="parentOperationId">Optional parent operation identifier.</param>
    /// <param name="tags">Optional tags associated with the operation.</param>
    public OperationSnapshot(
        OperationId operationId,
        string name,
        OperationStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? completedAtUtc = null,
        double? percentComplete = null,
        string? message = null,
        CorrelationContext? correlation = null,
        OperationId? parentOperationId = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Operation name is required.", nameof(name));
        }

        OperationId = operationId;
        Name = name.Trim();
        Status = status;
        StartedAtUtc = startedAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        CompletedAtUtc = completedAtUtc;
        PercentComplete = percentComplete;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        Correlation = correlation;
        ParentOperationId = parentOperationId;
        Tags = tags;
    }

    /// <summary>
    /// Gets the operation identifier.
    /// </summary>
    public OperationId OperationId { get; }

    /// <summary>
    /// Gets the operation name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the operation status.
    /// </summary>
    public OperationStatus Status { get; }

    /// <summary>
    /// Gets the start timestamp (UTC).
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    /// Gets the last update timestamp (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>
    /// Gets the completion timestamp (UTC).
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; }

    /// <summary>
    /// Gets the percentage complete (0-100).
    /// </summary>
    public double? PercentComplete { get; }

    /// <summary>
    /// Gets the progress message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the optional correlation context.
    /// </summary>
    public CorrelationContext? Correlation { get; }

    /// <summary>
    /// Gets the optional parent operation identifier.
    /// </summary>
    public OperationId? ParentOperationId { get; }

    /// <summary>
    /// Gets the optional tags.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; }
}
