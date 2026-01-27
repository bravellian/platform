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

using System.Threading;

namespace Bravellian.Platform.Email;

/// <summary>
/// In-memory implementation of <see cref="IEmailOutboxStore"/> for testing and development.
/// </summary>
public sealed class InMemoryEmailOutboxStore : IEmailOutboxStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, EmailOutboxEntry> entries = new();
    private readonly Dictionary<string, Guid> keyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryEmailOutboxStore"/> class.
    /// </summary>
    /// <param name="timeProvider">Time provider.</param>
    public InMemoryEmailOutboxStore(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<bool> AlreadyEnqueuedAsync(string messageKey, string providerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
        {
            throw new ArgumentException("Message key is required.", nameof(messageKey));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        var compositeKey = ComposeKey(providerName, messageKey);
        lock (sync)
        {
            return Task.FromResult(keyIndex.ContainsKey(compositeKey));
        }
    }

    /// <inheritdoc />
    public Task EnqueueAsync(EmailOutboxItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var compositeKey = ComposeKey(item.ProviderName, item.MessageKey);

        lock (sync)
        {
            if (keyIndex.ContainsKey(compositeKey))
            {
                return Task.CompletedTask;
            }

            var entry = new EmailOutboxEntry(item, EmailOutboxStatus.Pending, null);
            entries[item.Id] = entry;
            keyIndex[compositeKey] = item.Id;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailOutboxItem>> DequeueAsync(int maxItems, CancellationToken cancellationToken)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Batch size must be greater than zero.");
        }

        var now = timeProvider.GetUtcNow();
        List<EmailOutboxItem> results;

        lock (sync)
        {
            results = entries.Values
                .Where(entry => entry.Status == EmailOutboxStatus.Pending)
                .Where(entry => entry.Item.DueTimeUtc == null || entry.Item.DueTimeUtc <= now)
                .OrderBy(entry => entry.Item.EnqueuedAtUtc)
                .Take(maxItems)
                .Select(entry =>
                {
                    var updatedItem = new EmailOutboxItem(
                        entry.Item.Id,
                        entry.Item.ProviderName,
                        entry.Item.MessageKey,
                        entry.Item.Message,
                        entry.Item.EnqueuedAtUtc,
                        entry.Item.DueTimeUtc,
                        entry.Item.AttemptCount + 1);
                    entries[entry.Item.Id] = new EmailOutboxEntry(updatedItem, EmailOutboxStatus.Processing, entry.FailureReason);
                    return updatedItem;
                })
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<EmailOutboxItem>>(results);
    }

    /// <inheritdoc />
    public Task MarkSucceededAsync(Guid outboxId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (entries.TryGetValue(outboxId, out var entry))
            {
                entries[outboxId] = entry with { Status = EmailOutboxStatus.Succeeded, FailureReason = null };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid outboxId, string? failureReason, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (entries.TryGetValue(outboxId, out var entry))
            {
                entries[outboxId] = entry with { Status = EmailOutboxStatus.Failed, FailureReason = failureReason };
            }
        }

        return Task.CompletedTask;
    }

    internal bool TryGetEntry(
        Guid outboxId,
        out EmailOutboxStatus status,
        out string? failureReason,
        out int attemptCount)
    {
        lock (sync)
        {
            if (entries.TryGetValue(outboxId, out var entry))
            {
                status = entry.Status;
                failureReason = entry.FailureReason;
                attemptCount = entry.Item.AttemptCount;
                return true;
            }
        }

        status = EmailOutboxStatus.Pending;
        failureReason = null;
        attemptCount = 0;
        return false;
    }

    private static string ComposeKey(string providerName, string messageKey)
    {
        return $"{providerName}:{messageKey}";
    }

    private sealed record EmailOutboxEntry(EmailOutboxItem Item, EmailOutboxStatus Status, string? FailureReason);
}


