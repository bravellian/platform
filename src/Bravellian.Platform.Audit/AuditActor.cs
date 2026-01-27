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

namespace Bravellian.Platform.Audit;

/// <summary>
/// Describes the actor responsible for an audit event.
/// </summary>
public sealed record AuditActor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditActor"/> record.
    /// </summary>
    /// <param name="actorType">Actor type (e.g., User, System, ApiClient).</param>
    /// <param name="actorId">Actor identifier.</param>
    /// <param name="actorDisplay">Optional display name.</param>
    public AuditActor(string actorType, string actorId, string? actorDisplay)
    {
        if (string.IsNullOrWhiteSpace(actorType))
        {
            throw new ArgumentException("Actor type is required.", nameof(actorType));
        }

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }

        ActorType = actorType.Trim();
        ActorId = actorId.Trim();
        ActorDisplay = string.IsNullOrWhiteSpace(actorDisplay) ? null : actorDisplay.Trim();
    }

    /// <summary>
    /// Gets the actor type.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the actor identifier.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor display name.
    /// </summary>
    public string? ActorDisplay { get; }
}