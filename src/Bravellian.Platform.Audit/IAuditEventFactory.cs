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
/// Creates audit events from domain-specific inputs.
/// </summary>
public interface IAuditEventFactory
{
    /// <summary>
    /// Creates an audit event using the supplied values.
    /// </summary>
    /// <param name="eventId">Audit event identifier.</param>
    /// <param name="occurredAtUtc">Timestamp when the event occurred (UTC).</param>
    /// <param name="name">Stable event name.</param>
    /// <param name="displayMessage">Human-readable display message.</param>
    /// <param name="outcome">Event outcome.</param>
    /// <param name="anchors">Anchors associated with the event.</param>
    /// <param name="dataJson">Optional JSON payload.</param>
    /// <param name="actor">Optional actor context.</param>
    /// <param name="correlation">Optional correlation context.</param>
    /// <returns>Audit event instance.</returns>
    AuditEvent Create(
        AuditEventId eventId,
        DateTimeOffset occurredAtUtc,
        string name,
        string displayMessage,
        EventOutcome outcome,
        IReadOnlyList<EventAnchor> anchors,
        string? dataJson = null,
        AuditActor? actor = null,
        Bravellian.Platform.Correlation.CorrelationContext? correlation = null);
}