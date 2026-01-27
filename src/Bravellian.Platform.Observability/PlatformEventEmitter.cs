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
using Bravellian.Platform.Audit;
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Operations;

namespace Bravellian.Platform.Observability;

/// <summary>
/// Default implementation of <see cref="IPlatformEventEmitter"/>.
/// </summary>
public sealed class PlatformEventEmitter : IPlatformEventEmitter
{
    private readonly IAuditEventWriter auditWriter;
    private readonly IOperationTracker operationTracker;
    private readonly ICorrelationContextAccessor? correlationAccessor;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformEventEmitter"/> class.
    /// </summary>
    /// <param name="auditWriter">Audit event writer.</param>
    /// <param name="operationTracker">Operation tracker.</param>
    /// <param name="correlationAccessor">Optional correlation accessor.</param>
    /// <param name="timeProvider">Optional time provider for deterministic timestamps.</param>
    public PlatformEventEmitter(
        IAuditEventWriter auditWriter,
        IOperationTracker operationTracker,
        ICorrelationContextAccessor? correlationAccessor = null,
        TimeProvider? timeProvider = null)
    {
        this.auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        this.operationTracker = operationTracker ?? throw new ArgumentNullException(nameof(operationTracker));
        this.correlationAccessor = correlationAccessor;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<OperationId> EmitOperationStartedAsync(
        string name,
        CorrelationContext? correlationContext,
        OperationId? parentOperationId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken)
    {
        var correlation = correlationContext ?? correlationAccessor?.Current;
        var operationId = await operationTracker.StartAsync(
            name,
            correlation,
            parentOperationId,
            tags,
            cancellationToken).ConfigureAwait(false);

        var eventTags = MergeTags(tags, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformTagKeys.OperationId] = operationId.Value,
        });

        var auditEvent = BuildAuditEvent(
            PlatformEventNames.OperationStarted,
            $"Operation started: {name}",
            EventOutcome.Info,
            correlation,
            eventTags,
            operationId,
            null,
            message: null);

        await auditWriter.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        return operationId;
    }

    /// <inheritdoc />
    public async Task EmitOperationCompletedAsync(
        OperationId operationId,
        OperationStatus status,
        string? message,
        CorrelationContext? correlationContext,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken)
    {
        var correlation = correlationContext ?? correlationAccessor?.Current;
        await operationTracker.CompleteAsync(operationId, status, message, cancellationToken).ConfigureAwait(false);

        var eventTags = MergeTags(tags, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformTagKeys.OperationId] = operationId.Value,
        });

        var name = status == OperationStatus.Failed
            ? PlatformEventNames.OperationFailed
            : PlatformEventNames.OperationCompleted;

        var outcome = status switch
        {
            OperationStatus.Succeeded => EventOutcome.Success,
            OperationStatus.Failed => EventOutcome.Failure,
            OperationStatus.Canceled or OperationStatus.Stalled => EventOutcome.Warning,
            _ => EventOutcome.Info,
        };

        var display = message ?? $"Operation {status}: {operationId.Value}";

        var auditEvent = BuildAuditEvent(
            name,
            display,
            outcome,
            correlation,
            eventTags,
            operationId,
            status,
            message);

        await auditWriter.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task EmitAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        return auditWriter.WriteAsync(auditEvent, cancellationToken);
    }

    private AuditEvent BuildAuditEvent(
        string name,
        string displayMessage,
        EventOutcome outcome,
        CorrelationContext? correlation,
        IReadOnlyDictionary<string, string>? tags,
        OperationId operationId,
        OperationStatus? status,
        string? message)
    {
        var anchors = new[] { new EventAnchor("Operation", operationId.Value, "Subject") };
        var dataJson = BuildDataJson(status, message, tags);

        return new AuditEvent(
            AuditEventId.NewId(),
            timeProvider.GetUtcNow(),
            name,
            displayMessage,
            outcome,
            anchors,
            dataJson,
            actor: null,
            correlation: correlation);
    }

    private static string? BuildDataJson(
        OperationStatus? status,
        string? message,
        IReadOnlyDictionary<string, string>? tags)
    {
        if (status is null && string.IsNullOrWhiteSpace(message) && (tags is null || tags.Count == 0))
        {
            return null;
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = status?.ToString(),
            ["message"] = string.IsNullOrWhiteSpace(message) ? null : message,
            ["tags"] = tags,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyDictionary<string, string>? MergeTags(
        IReadOnlyDictionary<string, string>? tags,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (tags is null && overrides is null)
        {
            return null;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                merged[tag.Key] = tag.Value;
            }
        }

        if (overrides is not null)
        {
            foreach (var tag in overrides)
            {
                merged[tag.Key] = tag.Value;
            }
        }

        return merged;
    }
}