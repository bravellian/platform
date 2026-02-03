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

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Bravellian.Platform.Outbox;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Dispatches outbox messages for the global outbox.
/// </summary>
internal sealed class GlobalOutboxDispatcher
{
    private const string LeaseKey = "outbox-processing:global";
    private readonly IGlobalOutboxStore store;
    private readonly IOutboxHandlerResolver resolver;
    private readonly IGlobalSystemLeaseFactory leaseFactory;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<GlobalOutboxDispatcher> logger;
    private readonly TimeSpan leaseDuration;
    private readonly int maxAttempts;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalOutboxDispatcher"/> class.
    /// </summary>
    /// <param name="store">Global outbox store.</param>
    /// <param name="resolver">Outbox handler resolver.</param>
    /// <param name="leaseFactory">Lease factory for global processing coordination.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="backoffPolicy">Optional backoff policy.</param>
    /// <param name="leaseDuration">Optional lease duration override.</param>
    /// <param name="maxAttempts">Maximum attempts before failing a message.</param>
    public GlobalOutboxDispatcher(
        IGlobalOutboxStore store,
        IOutboxHandlerResolver resolver,
        IGlobalSystemLeaseFactory leaseFactory,
        ILogger<GlobalOutboxDispatcher> logger,
        Func<int, TimeSpan>? backoffPolicy = null,
        TimeSpan? leaseDuration = null,
        int maxAttempts = 5)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.leaseFactory = leaseFactory ?? throw new ArgumentNullException(nameof(leaseFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.backoffPolicy = backoffPolicy ?? MultiOutboxDispatcher.DefaultBackoff;
        this.leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);
        this.maxAttempts = maxAttempts;
    }

    /// <summary>
    /// Processes a single batch of global outbox messages.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dispatcher logs per-message failures and continues.")]
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        ISystemLease? lease = null;
        try
        {
            lease = await leaseFactory.AcquireAsync(
                LeaseKey,
                leaseDuration,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (lease == null)
            {
                logger.LogDebug("Global outbox lease is held by another instance");
                return 0;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.CancellationToken);
            return await ProcessStoreAsync(batchSize, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            if (lease != null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<int> ProcessStoreAsync(int batchSize, CancellationToken cancellationToken)
    {
        var messages = await store.ClaimDueAsync(batchSize, cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return 0;
        }

        logger.LogInformation("Processing {MessageCount} global outbox messages", messages.Count);

        var processedCount = 0;
        foreach (var message in messages)
        {
            try
            {
                await ProcessSingleMessageAsync(message, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(
                    "Global outbox processing cancelled after processing {ProcessedCount} of {TotalCount} messages",
                    processedCount,
                    messages.Count);
                break;
            }
        }

        logger.LogInformation(
            "Completed global outbox batch processing: {ProcessedCount}/{TotalCount} messages processed",
            processedCount,
            messages.Count);

        return processedCount;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dispatcher handles retries and failure reporting.")]
    private async Task ProcessSingleMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!resolver.TryGet(message.Topic, out var handler))
            {
                logger.LogWarning(
                    "No handler registered for topic '{Topic}' - failing global message {MessageId}",
                    message.Topic,
                    message.Id);
                await store.FailAsync(
                    message.Id,
                    $"No handler registered for topic '{message.Topic}'",
                    cancellationToken).ConfigureAwait(false);
                SchedulerMetrics.OutboxMessagesFailed.Add(1);
                return;
            }

            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
            await store.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesSent.Add(1);
        }
        catch (OutboxPermanentFailureException ex)
        {
            logger.LogWarning(
                ex,
                "Permanent failure processing global message {MessageId} with topic '{Topic}'. Marking as failed.",
                message.Id,
                message.Topic);
            await store.FailAsync(message.Id, ex.ToString(), cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesFailed.Add(1);
        }
        catch (Exception ex)
        {
            var nextAttempt = message.RetryCount + 1;
            if (nextAttempt >= maxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Handler failed for global message {MessageId} with topic '{Topic}' (attempt {AttemptCount}). Marking as permanently failed",
                    message.Id,
                    message.Topic,
                    nextAttempt);
                await store.FailAsync(message.Id, ex.ToString(), cancellationToken).ConfigureAwait(false);
                SchedulerMetrics.OutboxMessagesFailed.Add(1);
                return;
            }

            var delay = backoffPolicy(nextAttempt);
            logger.LogWarning(
                ex,
                "Handler failed for global message {MessageId} with topic '{Topic}' (attempt {AttemptCount}). Rescheduling with {DelayMs}ms delay",
                message.Id,
                message.Topic,
                nextAttempt,
                delay.TotalMilliseconds);
            await store.RescheduleAsync(message.Id, delay, ex.ToString(), cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesFailed.Add(1);
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.OutboxSendDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
