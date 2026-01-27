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

namespace Bravellian.Platform.Audit;

/// <summary>
/// Represents a single immutable audit event.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditEvent"/> record.
    /// </summary>
    /// <param name="eventId">Audit event identifier.</param>
    /// <param name="occurredAtUtc">Timestamp when the event occurred (UTC).</param>
    /// <param name="name">Stable event name.</param>
    /// <param name="displayMessage">Human-readable message.</param>
    /// <param name="outcome">Event outcome.</param>
    /// <param name="anchors">Anchors associated with the event.</param>
    /// <param name="dataJson">Optional JSON payload.</param>
    /// <param name="actor">Optional actor context.</param>
    /// <param name="correlation">Optional correlation context.</param>
    public AuditEvent(
        AuditEventId eventId,
        DateTimeOffset occurredAtUtc,
        string name,
        string displayMessage,
        EventOutcome outcome,
        IReadOnlyList<EventAnchor> anchors,
        string? dataJson = null,
        AuditActor? actor = null,
        CorrelationContext? correlation = null)
    {
        EventId = eventId;
        OccurredAtUtc = occurredAtUtc;
        Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        DisplayMessage = string.IsNullOrWhiteSpace(displayMessage) ? string.Empty : displayMessage.Trim();
        Outcome = outcome;
        Anchors = anchors ?? Array.Empty<EventAnchor>();
        DataJson = dataJson;
        Actor = actor;
        Correlation = correlation;
    }

    /// <summary>
    /// Gets the audit event identifier.
    /// </summary>
    public AuditEventId EventId { get; }

    /// <summary>
    /// Gets the timestamp when the event occurred (UTC).
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Gets the stable event name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the human-readable display message.
    /// </summary>
    public string DisplayMessage { get; }

    /// <summary>
    /// Gets the event outcome.
    /// </summary>
    public EventOutcome Outcome { get; }

    /// <summary>
    /// Gets the optional JSON payload.
    /// </summary>
    public string? DataJson { get; }

    /// <summary>
    /// Gets the anchors associated with the event.
    /// </summary>
    public IReadOnlyList<EventAnchor> Anchors { get; }

    /// <summary>
    /// Gets the optional actor context.
    /// </summary>
    public AuditActor? Actor { get; }

    /// <summary>
    /// Gets the optional correlation context.
    /// </summary>
    public CorrelationContext? Correlation { get; }
}
