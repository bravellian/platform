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
/// Represents a join (or job) that tracks a group of related outbox messages,
/// enabling fan-in semantics where a follow-up action occurs after all messages complete.
/// </summary>
public sealed record OutboxJoin
{
    /// <summary>
    /// Gets the unique identifier for this join.
    /// </summary>
    public JoinId JoinId { get; internal init; }

    /// <summary>
    /// Gets the PayeWaive tenant identifier for this join.
    /// </summary>
    public long PayeWaiveTenantId { get; internal init; }

    /// <summary>
    /// Gets the total number of steps expected to complete for this join.
    /// </summary>
    public int ExpectedSteps { get; internal init; }

    /// <summary>
    /// Gets the number of steps that have completed successfully.
    /// </summary>
    public int CompletedSteps { get; internal init; }

    /// <summary>
    /// Gets the number of steps that have failed.
    /// </summary>
    public int FailedSteps { get; internal init; }

    /// <summary>
    /// Gets the current status of the join (Pending, Completed, Failed, or Cancelled).
    /// </summary>
    public byte Status { get; internal init; }

    /// <summary>
    /// Gets the timestamp when this join was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; internal init; }

    /// <summary>
    /// Gets the timestamp when this join was last updated.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; internal init; }

    /// <summary>
    /// Gets optional metadata for the join (e.g., join type, description, configuration).
    /// Stored as JSON.
    /// </summary>
    public string? Metadata { get; internal init; }
}
