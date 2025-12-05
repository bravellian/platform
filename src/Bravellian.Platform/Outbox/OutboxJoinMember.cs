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
/// Represents the association between an outbox join and an outbox message.
/// This many-to-many relationship allows:
/// - One join to track multiple messages
/// - One message to participate in multiple joins
/// </summary>
public sealed record OutboxJoinMember
{
    /// <summary>
    /// Gets the join identifier.
    /// </summary>
    public Guid JoinId { get; internal init; }

    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid OutboxMessageId { get; internal init; }

    /// <summary>
    /// Gets the timestamp when this association was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; internal init; }

    /// <summary>
    /// Gets the timestamp when this member was marked as completed, or null if not completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; internal init; }

    /// <summary>
    /// Gets the timestamp when this member was marked as failed, or null if not failed.
    /// </summary>
    public DateTimeOffset? FailedAt { get; internal init; }
}
