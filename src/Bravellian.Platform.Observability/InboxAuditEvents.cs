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

using System.Text.Json;
using System.Text.Json.Serialization;
using Bravellian.Platform.Audit;

namespace Bravellian.Platform.Observability;

/// <summary>
/// Emits audit events for inbox recovery operations.
/// </summary>
public static class InboxAuditEvents
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Emits an audit event for an inbox revive operation.
    /// </summary>
    /// <param name="emitter">Optional platform event emitter.</param>
    /// <param name="message">Inbox message snapshot prior to revival.</param>
    /// <param name="reason">Optional reason for the revive.</param>
    /// <param name="delay">Optional delay before the message becomes eligible again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the audit event is emitted.</returns>
    public static Task EmitRevivedAsync(
        IPlatformEventEmitter? emitter,
        InboxMessageSnapshot message,
        string? reason,
        TimeSpan? delay,
        CancellationToken cancellationToken)
    {
        if (emitter is null)
        {
            return Task.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(message);

        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PlatformTagKeys.InboxMessageId] = message.MessageId,
            ["source"] = message.Source,
            ["topic"] = message.Topic,
            ["attempt"] = message.Attempt,
            ["lastError"] = message.LastError,
            ["dueTimeUtc"] = message.DueTimeUtc,
            ["reviveReason"] = NormalizeReason(reason),
            ["reviveDelayMs"] = delay?.TotalMilliseconds,
        };

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            DateTimeOffset.UtcNow,
            PlatformEventNames.InboxMessageRevived,
            "Inbox message revived",
            EventOutcome.Info,
            new[] { new EventAnchor("Inbox", message.MessageId, "MessageId") },
            JsonSerializer.Serialize(data, SerializerOptions));

        return emitter.EmitAuditEventAsync(auditEvent, cancellationToken);
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
