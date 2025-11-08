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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dispatches outbox messages across multiple databases/tenants using a pluggable
/// selection strategy to determine which outbox to poll next.
/// This enables processing messages from multiple customer databases in a single worker.
/// </summary>
internal sealed class MultiOutboxDispatcher
{
    private readonly IOutboxStoreProvider storeProvider;
    private readonly IOutboxSelectionStrategy selectionStrategy;
    private readonly IOutboxHandlerResolver resolver;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<MultiOutboxDispatcher> logger;

    private IOutboxStore? lastProcessedStore;
    private int lastProcessedCount;

    public MultiOutboxDispatcher(
        IOutboxStoreProvider storeProvider,
        IOutboxSelectionStrategy selectionStrategy,
        IOutboxHandlerResolver resolver,
        ILogger<MultiOutboxDispatcher> logger,
        Func<int, TimeSpan>? backoffPolicy = null)
    {
        this.storeProvider = storeProvider;
        this.selectionStrategy = selectionStrategy;
        this.resolver = resolver;
        this.logger = logger;
        this.backoffPolicy = backoffPolicy ?? OutboxDispatcher.DefaultBackoff;
    }

    /// <summary>
    /// Processes a single batch of outbox messages from the next selected store.
    /// Uses the selection strategy to determine which outbox to poll.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var stores = this.storeProvider.GetAllStores();

        if (stores.Count == 0)
        {
            this.logger.LogDebug("No outbox stores available for processing");
            return 0;
        }

        // Use the selection strategy to pick the next store
        var selectedStore = this.selectionStrategy.SelectNext(
            stores,
            this.lastProcessedStore,
            this.lastProcessedCount);

        if (selectedStore == null)
        {
            this.logger.LogDebug("Selection strategy returned no store to process");
            return 0;
        }

        var storeIdentifier = this.storeProvider.GetStoreIdentifier(selectedStore);
        this.logger.LogDebug(
            "Processing outbox messages from store '{StoreIdentifier}' with batch size {BatchSize}",
            storeIdentifier,
            batchSize);

        var messages = await selectedStore.ClaimDueAsync(batchSize, cancellationToken).ConfigureAwait(false);

        if (messages.Count == 0)
        {
            this.logger.LogDebug("No messages available in store '{StoreIdentifier}'", storeIdentifier);
            this.lastProcessedStore = selectedStore;
            this.lastProcessedCount = 0;
            return 0;
        }

        this.logger.LogInformation(
            "Processing {MessageCount} outbox messages from store '{StoreIdentifier}'",
            messages.Count,
            storeIdentifier);

        var processedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                await this.ProcessSingleMessageAsync(
                    selectedStore,
                    storeIdentifier,
                    message,
                    cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.LogDebug(
                    "Outbox processing cancelled after processing {ProcessedCount} of {TotalCount} messages from store '{StoreIdentifier}'",
                    processedCount,
                    messages.Count,
                    storeIdentifier);

                // Stop processing if cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                // Log unexpected errors but continue processing other messages
                this.logger.LogError(
                    ex,
                    "Unexpected error processing outbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}'",
                    message.Id,
                    message.Topic,
                    storeIdentifier);
            }
        }

        this.logger.LogInformation(
            "Completed outbox batch processing from store '{StoreIdentifier}': {ProcessedCount}/{TotalCount} messages processed",
            storeIdentifier,
            processedCount,
            messages.Count);

        this.lastProcessedStore = selectedStore;
        this.lastProcessedCount = processedCount;

        return processedCount;
    }

    private async Task ProcessSingleMessageAsync(
        IOutboxStore store,
        string storeIdentifier,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            this.logger.LogDebug(
                "Processing outbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}'",
                message.Id,
                message.Topic,
                storeIdentifier);

            // Try to resolve handler for this topic
            if (!this.resolver.TryGet(message.Topic, out var handler))
            {
                this.logger.LogWarning(
                    "No handler registered for topic '{Topic}' - failing message {MessageId} from store '{StoreIdentifier}'",
                    message.Topic,
                    message.Id,
                    storeIdentifier);
                await store.FailAsync(
                    message.Id,
                    $"No handler registered for topic '{message.Topic}'",
                    cancellationToken).ConfigureAwait(false);
                SchedulerMetrics.OutboxMessagesFailed.Add(1);
                return;
            }

            // Execute the handler
            this.logger.LogDebug(
                "Executing handler for message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}'",
                message.Id,
                message.Topic,
                storeIdentifier);
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            // Mark as successfully dispatched
            this.logger.LogDebug(
                "Successfully processed message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}'",
                message.Id,
                message.Topic,
                storeIdentifier);
            await store.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesSent.Add(1);
        }
        catch (Exception ex)
        {
            // Handler threw an exception - reschedule with backoff
            var nextAttempt = message.RetryCount + 1;
            var delay = this.backoffPolicy(nextAttempt);

            this.logger.LogWarning(
                ex,
                "Handler failed for message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}' (attempt {AttemptCount}). Rescheduling with {DelayMs}ms delay",
                message.Id,
                message.Topic,
                storeIdentifier,
                nextAttempt,
                delay.TotalMilliseconds);

            await store.RescheduleAsync(message.Id, delay, ex.Message, cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesFailed.Add(1);
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.OutboxSendDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
