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

using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Observability;

/// <summary>
/// Coordinates inbox revive operations with audit events.
/// </summary>
public sealed class InboxRecoveryService
{
    private readonly IInboxWorkStore workStore;
    private readonly IPlatformEventEmitter? eventEmitter;
    private readonly ILogger<InboxRecoveryService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxRecoveryService"/> class.
    /// </summary>
    /// <param name="workStore">Inbox work store.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    public InboxRecoveryService(
        IInboxWorkStore workStore,
        ILogger<InboxRecoveryService> logger,
        IPlatformEventEmitter? eventEmitter = null)
    {
        this.workStore = workStore ?? throw new ArgumentNullException(nameof(workStore));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.eventEmitter = eventEmitter;
    }

    /// <summary>
    /// Revives dead inbox messages and emits audit events containing the prior error state.
    /// </summary>
    /// <param name="messageIds">Message identifiers to revive.</param>
    /// <param name="reason">Optional reason for the revive.</param>
    /// <param name="delay">Optional delay before reprocessing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReviveAsync(
        IEnumerable<InboxMessageIdentifier> messageIds,
        string? reason = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        var idList = messageIds.Where(id => !string.IsNullOrWhiteSpace(id.Value)).Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        var snapshots = await CaptureSnapshotsAsync(idList, cancellationToken).ConfigureAwait(false);
        var normalizedReason = NormalizeReason(reason);

        await workStore.ReviveAsync(idList, normalizedReason, delay, cancellationToken).ConfigureAwait(false);

        foreach (var snapshot in snapshots)
        {
            await InboxAuditEvents.EmitRevivedAsync(
                eventEmitter,
                snapshot,
                normalizedReason,
                delay,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<InboxMessageSnapshot>> CaptureSnapshotsAsync(
        List<InboxMessageIdentifier> messageIds,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<InboxMessageSnapshot>(messageIds.Count);

        foreach (var messageId in messageIds)
        {
            try
            {
                var message = await workStore.GetAsync(messageId, cancellationToken).ConfigureAwait(false);
                snapshots.Add(new InboxMessageSnapshot(
                    message.MessageId,
                    message.Source,
                    message.Topic,
                    message.Attempt,
                    message.LastError,
                    message.DueTimeUtc));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to load inbox message {MessageId} for revive audit event.",
                    messageId);

                snapshots.Add(new InboxMessageSnapshot(
                    messageId,
                    Source: null,
                    Topic: null,
                    Attempt: null,
                    LastError: null,
                    DueTimeUtc: null));
            }
        }

        return snapshots;
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
