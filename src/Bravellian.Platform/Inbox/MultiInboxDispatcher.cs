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
/// Dispatches inbox messages across multiple databases/tenants using a pluggable
/// selection strategy to determine which inbox to poll next.
/// This enables processing messages from multiple customer databases in a single worker.
/// </summary>
public sealed class MultiInboxDispatcher
{
    private readonly IInboxWorkStoreProvider storeProvider;
    private readonly IInboxSelectionStrategy selectionStrategy;
    private readonly IInboxHandlerResolver resolver;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<MultiInboxDispatcher> logger;
    private readonly int maxAttempts;
    private readonly int leaseSeconds;
    private readonly object stateLock = new();

    private IInboxWorkStore? lastProcessedStore;
    private int lastProcessedCount;

    public MultiInboxDispatcher(
        IInboxWorkStoreProvider storeProvider,
        IInboxSelectionStrategy selectionStrategy,
        IInboxHandlerResolver resolver,
        ILogger<MultiInboxDispatcher> logger,
        Func<int, TimeSpan>? backoffPolicy = null,
        int maxAttempts = 5,
        int leaseSeconds = 30)
    {
        this.storeProvider = storeProvider;
        this.selectionStrategy = selectionStrategy;
        this.resolver = resolver;
        this.logger = logger;
        this.backoffPolicy = backoffPolicy ?? InboxDispatcher.DefaultBackoff;
        this.maxAttempts = maxAttempts;
        this.leaseSeconds = leaseSeconds;
    }

    /// <summary>
    /// Processes a single batch of inbox messages from the next selected store.
    /// Uses the selection strategy to determine which inbox to poll.
    /// This method is thread-safe and can be called concurrently, though it's typically
    /// called by a single background thread in MultiInboxPollingService.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var stores = this.storeProvider.GetAllStores();

        if (stores.Count == 0)
        {
            this.logger.LogDebug("No inbox work stores available for processing");
            return 0;
        }

        // Use the selection strategy to pick the next store
        IInboxWorkStore? selectedStore;
        lock (this.stateLock)
        {
            selectedStore = this.selectionStrategy.SelectNext(
                stores,
                this.lastProcessedStore,
                this.lastProcessedCount);
        }

        if (selectedStore == null)
        {
            this.logger.LogDebug("Selection strategy returned no store to process");
            return 0;
        }

        var storeIdentifier = this.storeProvider.GetStoreIdentifier(selectedStore);
        var ownerToken = Guid.NewGuid();

        this.logger.LogDebug(
            "Processing inbox messages from store '{StoreIdentifier}' with batch size {BatchSize} and owner {OwnerToken}",
            storeIdentifier,
            batchSize,
            ownerToken);

        try
        {
            // Claim messages with a lease
            var claimedIds = await selectedStore.ClaimAsync(ownerToken, this.leaseSeconds, batchSize, cancellationToken).ConfigureAwait(false);

            if (claimedIds.Count == 0)
            {
                this.logger.LogDebug("No messages claimed from store '{StoreIdentifier}'", storeIdentifier);
                this.lastProcessedStore = selectedStore;
                this.lastProcessedCount = 0;
                return 0;
            }

            this.logger.LogInformation(
                "Processing {MessageCount} inbox messages from store '{StoreIdentifier}'",
                claimedIds.Count,
                storeIdentifier);

            var succeeded = new List<string>();
            var failed = new List<string>();

            // Process each claimed message
            foreach (var messageId in claimedIds)
            {
                var wasHandled = await this.ProcessSingleMessageAsync(
                    selectedStore,
                    storeIdentifier,
                    ownerToken,
                    messageId,
                    cancellationToken).ConfigureAwait(false);

                if (wasHandled)
                {
                    succeeded.Add(messageId);
                }
                else
                {
                    failed.Add(messageId);
                }
            }

            // Acknowledge successfully processed messages
            if (succeeded.Count > 0)
            {
                await selectedStore.AckAsync(ownerToken, succeeded, cancellationToken).ConfigureAwait(false);
                this.logger.LogDebug(
                    "Acknowledged {SucceededCount} successfully processed inbox messages from store '{StoreIdentifier}'",
                    succeeded.Count,
                    storeIdentifier);
            }

            // Handle failed messages
            if (failed.Count > 0)
            {
                await this.HandleFailedMessagesAsync(
                    selectedStore,
                    storeIdentifier,
                    ownerToken,
                    failed,
                    cancellationToken).ConfigureAwait(false);
            }

            this.logger.LogInformation(
                "Completed inbox batch processing from store '{StoreIdentifier}': {TotalProcessed} messages, {Succeeded} succeeded, {Failed} failed",
                storeIdentifier,
                claimedIds.Count,
                succeeded.Count,
                failed.Count);

            lock (this.stateLock)
            {
                this.lastProcessedStore = selectedStore;
                this.lastProcessedCount = claimedIds.Count;
            }

            return claimedIds.Count;
        }
        catch (Exception ex)
        {
            this.logger.LogError(
                ex,
                "Failed to process inbox batch from store '{StoreIdentifier}' with owner {OwnerToken}",
                storeIdentifier,
                ownerToken);
            throw;
        }
    }

    private async Task<bool> ProcessSingleMessageAsync(
        IInboxWorkStore store,
        string storeIdentifier,
        Guid ownerToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get the full message details
            var message = await store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);

            this.logger.LogDebug(
                "Processing inbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}' (attempt {Attempt})",
                message.MessageId,
                message.Topic,
                storeIdentifier,
                message.Attempt);

            // Resolve handler for this topic
            IInboxHandler handler;
            try
            {
                handler = this.resolver.GetHandler(message.Topic);
            }
            catch (InvalidOperationException)
            {
                this.logger.LogWarning(
                    "No handler registered for topic '{Topic}' - marking message {MessageId} from store '{StoreIdentifier}' as dead",
                    message.Topic,
                    message.MessageId,
                    storeIdentifier);
                await store.FailAsync(ownerToken, new[] { messageId }, $"No handler registered for topic '{message.Topic}'", cancellationToken).ConfigureAwait(false);
                return true; // Message was handled (failed immediately)
            }

            // Execute the handler
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            this.logger.LogDebug(
                "Successfully processed inbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}' in {ElapsedMs}ms",
                message.MessageId,
                message.Topic,
                storeIdentifier,
                stopwatch.ElapsedMilliseconds);

            return true; // Message was handled successfully
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(
                ex,
                "Handler failed for inbox message {MessageId} from store '{StoreIdentifier}': {ErrorMessage}",
                messageId,
                storeIdentifier,
                ex.Message);
            return false; // Message failed and needs retry logic
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task HandleFailedMessagesAsync(
        IInboxWorkStore store,
        string storeIdentifier,
        Guid ownerToken,
        IList<string> failedMessageIds,
        CancellationToken cancellationToken)
    {
        var toAbandon = new List<string>();
        var toFail = new List<string>();

        // Determine which messages should be retried vs. marked as dead
        foreach (var messageId in failedMessageIds)
        {
            try
            {
                var message = await store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);

                if (message.Attempt >= this.maxAttempts)
                {
                    this.logger.LogWarning(
                        "Inbox message {MessageId} from store '{StoreIdentifier}' has reached max attempts ({MaxAttempts}), marking as dead",
                        messageId,
                        storeIdentifier,
                        this.maxAttempts);
                    toFail.Add(messageId);
                }
                else
                {
                    var delay = this.backoffPolicy(message.Attempt + 1);
                    this.logger.LogDebug(
                        "Inbox message {MessageId} from store '{StoreIdentifier}' will be retried after {DelayMs}ms delay (attempt {NextAttempt})",
                        messageId,
                        storeIdentifier,
                        delay.TotalMilliseconds,
                        message.Attempt + 1);
                    toAbandon.Add(messageId);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(
                    ex,
                    "Failed to determine retry policy for message {MessageId} from store '{StoreIdentifier}', abandoning for retry",
                    messageId,
                    storeIdentifier);
                toAbandon.Add(messageId);
            }
        }

        // Abandon messages that should be retried
        if (toAbandon.Count > 0)
        {
            await store.AbandonAsync(ownerToken, toAbandon, cancellationToken).ConfigureAwait(false);
        }

        // Fail messages that have exceeded max attempts
        if (toFail.Count > 0)
        {
            await store.FailAsync(ownerToken, toFail, "Maximum retry attempts exceeded", cancellationToken).ConfigureAwait(false);
        }
    }
}
