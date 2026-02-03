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
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Dispatches inbox messages across multiple databases/tenants using a pluggable
/// selection strategy to determine which inbox to poll next.
/// This enables processing messages from multiple customer databases in a single worker.
/// </summary>
internal sealed class MultiInboxDispatcher
{
    private readonly IInboxWorkStoreProvider storeProvider;
    private readonly IInboxSelectionStrategy selectionStrategy;
    private readonly IInboxHandlerResolver resolver;
    private readonly ILeaseRouter? leaseRouter;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<MultiInboxDispatcher> logger;
    private readonly int maxAttempts;
    private readonly int leaseSeconds;
    private readonly TimeSpan leaseDuration;
    private readonly Lock stateLock = new();

    private IInboxWorkStore? lastProcessedStore;
    private int lastProcessedCount;

    public MultiInboxDispatcher(
        IInboxWorkStoreProvider storeProvider,
        IInboxSelectionStrategy selectionStrategy,
        IInboxHandlerResolver resolver,
        ILogger<MultiInboxDispatcher> logger,
        ILeaseRouter? leaseRouter = null,
        Func<int, TimeSpan>? backoffPolicy = null,
        int maxAttempts = 5,
        int leaseSeconds = 30,
        TimeSpan? leaseDuration = null)
    {
        this.storeProvider = storeProvider;
        this.selectionStrategy = selectionStrategy;
        this.resolver = resolver;
        this.logger = logger;
        this.leaseRouter = leaseRouter;
        this.backoffPolicy = backoffPolicy ?? DefaultBackoff;
        this.maxAttempts = maxAttempts;
        this.leaseSeconds = leaseSeconds;
        this.leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Processes a single batch of inbox messages from the next selected store.
    /// Uses the selection strategy to determine which inbox to poll.
    /// Acquires a tenant-level lease before processing to ensure only one worker processes messages at a time.
    /// This method is thread-safe and can be called concurrently, though it's typically
    /// called by a single background thread in MultiInboxPollingService.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var stores = await storeProvider.GetAllStoresAsync().ConfigureAwait(false);

        if (stores.Count == 0)
        {
            logger.LogDebug("No inbox work stores available for processing");
            return 0;
        }

        // Use the selection strategy to pick the next store
        IInboxWorkStore? selectedStore;
        lock (stateLock)
        {
            selectedStore = selectionStrategy.SelectNext(
                stores,
                lastProcessedStore,
                lastProcessedCount);
        }

        if (selectedStore == null)
        {
            logger.LogDebug("Selection strategy returned no store to process");
            return 0;
        }

        var storeIdentifier = storeProvider.GetStoreIdentifier(selectedStore);

        // Try to acquire a lease for this tenant before processing
        if (leaseRouter != null)
        {
            ISystemLease? lease = null;
            try
            {
                var leaseFactory = await leaseRouter.GetLeaseFactoryAsync(storeIdentifier, cancellationToken).ConfigureAwait(false);
                lease = await leaseFactory.AcquireAsync(
                    "inbox-processing",
                    leaseDuration,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (lease == null)
                {
                    logger.LogDebug(
                        "Could not acquire lease for inbox processing on store '{StoreIdentifier}', skipping this iteration",
                        storeIdentifier);
                    lock (stateLock)
                    {
                        lastProcessedStore = selectedStore;
                        lastProcessedCount = 0;
                    }
                    return 0;
                }

                logger.LogDebug(
                    "Acquired lease for inbox processing on store '{StoreIdentifier}' with owner {OwnerToken}",
                    storeIdentifier,
                    lease.OwnerToken);

                // Process messages while holding the lease
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.CancellationToken);
                return await ProcessStoreAsync(selectedStore, storeIdentifier, batchSize, linkedCts.Token).ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                // No lease factory configured for this tenant, proceed without lease
                logger.LogWarning(
                    "No lease factory found for store '{StoreIdentifier}', processing without lease",
                    storeIdentifier);
                return await ProcessStoreAsync(selectedStore, storeIdentifier, batchSize, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lease != null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        else
        {
            // No lease router configured, process without lease
            return await ProcessStoreAsync(selectedStore, storeIdentifier, batchSize, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> ProcessStoreAsync(
        IInboxWorkStore selectedStore,
        string storeIdentifier,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        using var batchScope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ownerToken"] = ownerToken.ToString(),
            ["store"] = storeIdentifier,
        });

        logger.LogDebug(
            "Processing inbox messages from store '{StoreIdentifier}' with batch size {BatchSize} and owner {OwnerToken}",
            storeIdentifier,
            batchSize,
            ownerToken);

        try
        {
            // Claim messages with a lease
            var claimedIds = await selectedStore.ClaimAsync(ownerToken, leaseSeconds, batchSize, cancellationToken).ConfigureAwait(false);

            if (claimedIds.Count == 0)
            {
                logger.LogDebug("No messages claimed from store '{StoreIdentifier}'", storeIdentifier);
                lock (stateLock)
                {
                    lastProcessedStore = selectedStore;
                    lastProcessedCount = 0;
                }
                return 0;
            }

            logger.LogInformation(
                "Processing {MessageCount} inbox messages from store '{StoreIdentifier}'",
                claimedIds.Count,
                storeIdentifier);

            var succeeded = new List<string>();
            var failed = new Dictionary<string, string>(StringComparer.Ordinal); // messageId -> error message
            var permanentFailures = new Dictionary<string, string>(StringComparer.Ordinal); // messageId -> error message

            // Process each claimed message
            foreach (var messageId in claimedIds)
            {
                var (wasHandled, errorMessage, isPermanentFailure) = await ProcessSingleMessageAsync(
                    selectedStore,
                    storeIdentifier,
                    ownerToken,
                    messageId,
                    cancellationToken).ConfigureAwait(false);

                if (wasHandled)
                {
                    succeeded.Add(messageId);
                }
                else if (isPermanentFailure)
                {
                    permanentFailures[messageId] = errorMessage;
                }
                else
                {
                    failed[messageId] = errorMessage;
                }
            }

            // Acknowledge successfully processed messages
            if (succeeded.Count > 0)
            {
                await selectedStore.AckAsync(ownerToken, succeeded, cancellationToken).ConfigureAwait(false);
                logger.LogDebug(
                    "Acknowledged {SucceededCount} successfully processed inbox messages from store '{StoreIdentifier}'",
                    succeeded.Count,
                    storeIdentifier);
            }

            // Permanently fail messages that requested immediate failure
            foreach (var (messageId, errorMessage) in permanentFailures)
            {
                await selectedStore.FailAsync(ownerToken, new[] { messageId }, errorMessage, cancellationToken).ConfigureAwait(false);
            }

            // Handle failed messages
            if (failed.Count > 0)
            {
                await HandleFailedMessagesAsync(
                    selectedStore,
                    storeIdentifier,
                    ownerToken,
                    failed,
                    cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation(
                "Completed inbox batch processing from store '{StoreIdentifier}': {TotalProcessed} messages, {Succeeded} succeeded, {Failed} failed",
                storeIdentifier,
                claimedIds.Count,
                succeeded.Count,
                failed.Count + permanentFailures.Count);

            lock (stateLock)
            {
                lastProcessedStore = selectedStore;
                lastProcessedCount = claimedIds.Count;
            }

            return claimedIds.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process inbox batch from store '{StoreIdentifier}' with owner {OwnerToken}",
                storeIdentifier,
                ownerToken);
            throw;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dispatcher captures handler failures for retry handling.")]
    private async Task<(bool Success, string ErrorMessage, bool PermanentFailure)> ProcessSingleMessageAsync(
        IInboxWorkStore store,
        string storeIdentifier,
        Bravellian.Platform.OwnerToken ownerToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        using var messageScope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ownerToken"] = ownerToken.ToString(),
            ["workItemId"] = messageId,
            ["store"] = storeIdentifier,
        });

        try
        {
            // Get the full message details
            var message = await store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Processing inbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}' (attempt {Attempt})",
                message.MessageId,
                message.Topic,
                storeIdentifier,
                message.Attempt);

            // Resolve handler for this topic
            IInboxHandler handler;
            try
            {
                handler = resolver.GetHandler(message.Topic);
            }
            catch (InvalidOperationException)
            {
                logger.LogWarning(
                    "No handler registered for topic '{Topic}' - marking message {MessageId} from store '{StoreIdentifier}' as dead",
                    message.Topic,
                    message.MessageId,
                    storeIdentifier);
                await store.FailAsync(ownerToken, new[] { messageId }, $"No handler registered for topic '{message.Topic}'", cancellationToken).ConfigureAwait(false);
                return (true, string.Empty, false); // Message was handled (failed immediately)
            }

            // Execute the handler
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Successfully processed inbox message {MessageId} with topic '{Topic}' from store '{StoreIdentifier}' in {ElapsedMs}ms",
                message.MessageId,
                message.Topic,
                storeIdentifier,
                stopwatch.ElapsedMilliseconds);

            return (true, string.Empty, false); // Message was handled successfully
        }
        catch (InboxPermanentFailureException ex)
        {
            logger.LogWarning(
                ex,
                "Permanent failure processing inbox message {MessageId} from store '{StoreIdentifier}': {ErrorMessage}",
                messageId,
                storeIdentifier,
                ex.ToString());
            return (false, ex.ToString(), true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Handler failed for inbox message {MessageId} from store '{StoreIdentifier}': {ErrorMessage}",
                messageId,
                storeIdentifier,
                ex.ToString());
            return (false, ex.ToString(), false); // Return the current error message
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dispatcher logs failures and continues processing.")]
    private async Task HandleFailedMessagesAsync(
        IInboxWorkStore store,
        string storeIdentifier,
        Bravellian.Platform.OwnerToken ownerToken,
        IDictionary<string, string> failedMessages,
        CancellationToken cancellationToken)
    {
        var toAbandon = new List<(string MessageId, TimeSpan Delay, string Error)>();
        var toFail = new List<string>();

        // Determine which messages should be retried vs. marked as dead
        foreach (var (messageId, errorMessage) in failedMessages)
        {
            try
            {
                var message = await store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);

                if (message.Attempt >= maxAttempts)
                {
                    logger.LogWarning(
                        "Inbox message {MessageId} from store '{StoreIdentifier}' has reached max attempts ({MaxAttempts}), marking as dead",
                        messageId,
                        storeIdentifier,
                        maxAttempts);
                    toFail.Add(messageId);
                }
                else
                {
                    var delay = backoffPolicy(message.Attempt + 1);
                    logger.LogDebug(
                        "Inbox message {MessageId} from store '{StoreIdentifier}' will be retried after {DelayMs}ms delay (attempt {NextAttempt})",
                        messageId,
                        storeIdentifier,
                        delay.TotalMilliseconds,
                        message.Attempt + 1);
                    toAbandon.Add((messageId, delay, errorMessage));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to determine retry policy for message {MessageId} from store '{StoreIdentifier}', abandoning for immediate retry",
                    messageId,
                    storeIdentifier);
                toAbandon.Add((messageId, TimeSpan.Zero, ex.ToString()));
            }
        }

        // Abandon each message individually to preserve its specific error
        foreach (var (messageId, delay, error) in toAbandon)
        {
            await store.AbandonAsync(
                ownerToken,
                new[] { messageId },
                error,
                delay,
                cancellationToken).ConfigureAwait(false);
        }

        // Fail messages that have exceeded max attempts
        if (toFail.Count > 0)
        {
            await store.FailAsync(ownerToken, toFail, "Maximum retry attempts exceeded", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Default exponential backoff policy with jitter used when no custom policy is supplied.
    /// Mirrors the previous single-database implementation.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <returns>Delay before next attempt.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Jitter is used for retry dispersion, not security.")]
    internal static TimeSpan DefaultBackoff(int attempt)
    {
        var baseMs = Math.Min(60_000, (int)(Math.Pow(2, Math.Min(10, attempt)) * 250)); // 250ms, 500ms, 1s, ...
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }
}
