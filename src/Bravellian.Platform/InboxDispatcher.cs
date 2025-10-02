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

using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Dispatches inbox messages to their appropriate handlers.
/// This is the core processing engine that claims messages, resolves handlers, and manages retries.
/// Mirrors the OutboxDispatcher implementation for consistency.
/// </summary>
public sealed class InboxDispatcher
{
    private readonly IInboxWorkStore store;
    private readonly IInboxHandlerResolver resolver;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<InboxDispatcher> logger;
    private readonly int maxAttempts;

    public InboxDispatcher(
        IInboxWorkStore store,
        IInboxHandlerResolver resolver,
        ILogger<InboxDispatcher> logger,
        Func<int, TimeSpan>? backoffPolicy = null)
    {
        this.store = store;
        this.resolver = resolver;
        this.logger = logger;
        this.backoffPolicy = backoffPolicy ?? DefaultBackoff;
        this.maxAttempts = 5; // Default max attempts
    }

    /// <summary>
    /// Processes a single batch of inbox messages.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var ownerToken = Guid.NewGuid();
        var leaseSeconds = 30; // 30-second lease by default

        this.logger.LogDebug(
            "Starting inbox processing batch with owner {OwnerToken}, batch size {BatchSize}", 
            ownerToken, batchSize);

        try
        {
            // Claim messages with a lease
            var claimedIds = await this.store.ClaimAsync(ownerToken, leaseSeconds, batchSize, cancellationToken).ConfigureAwait(false);
            
            if (claimedIds.Count == 0)
            {
                this.logger.LogDebug("No inbox messages claimed for processing");
                return 0;
            }

            this.logger.LogDebug(
                "Claimed {ClaimedCount} inbox messages for processing with owner {OwnerToken}", 
                claimedIds.Count, ownerToken);

            var succeeded = new List<string>();
            var failed = new List<string>();

            // Process each claimed message
            foreach (var messageId in claimedIds)
            {
                try
                {
                    await ProcessSingleMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
                    succeeded.Add(messageId);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, 
                        "Failed to process inbox message {MessageId} with owner {OwnerToken}", 
                        messageId, ownerToken);
                    failed.Add(messageId);
                }
            }

            // Acknowledge successfully processed messages
            if (succeeded.Count > 0)
            {
                await this.store.AckAsync(ownerToken, succeeded, cancellationToken).ConfigureAwait(false);
                this.logger.LogDebug(
                    "Acknowledged {SucceededCount} successfully processed inbox messages", 
                    succeeded.Count);
            }

            // Handle failed messages
            if (failed.Count > 0)
            {
                await HandleFailedMessagesAsync(ownerToken, failed, cancellationToken).ConfigureAwait(false);
            }

            this.logger.LogDebug(
                "Completed inbox processing batch: {TotalProcessed} messages, {Succeeded} succeeded, {Failed} failed", 
                claimedIds.Count, succeeded.Count, failed.Count);

            return claimedIds.Count;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, 
                "Failed to process inbox batch with owner {OwnerToken}", 
                ownerToken);
            throw;
        }
    }

    /// <summary>
    /// Default exponential backoff policy with jitter.
    /// Mirrors the OutboxDispatcher.DefaultBackoff implementation.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <returns>Delay before next attempt.</returns>
    public static TimeSpan DefaultBackoff(int attempt)
    {
        // Exponential w/ cap + jitter
        var baseMs = Math.Min(60_000, (int)(Math.Pow(2, Math.Min(10, attempt)) * 250)); // 250ms, 500ms, 1s, ...
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    private async Task ProcessSingleMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get the full message details
            var message = await this.store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);

            this.logger.LogDebug(
                "Processing inbox message {MessageId} with topic '{Topic}' (attempt {Attempt})", 
                message.MessageId, message.Topic, message.Attempt);

            // Resolve handler for this topic
            IInboxHandler handler;
            try
            {
                handler = this.resolver.GetHandler(message.Topic);
            }
            catch (InvalidOperationException)
            {
                this.logger.LogWarning(
                    "No handler registered for topic '{Topic}' - message {MessageId} will be marked as failed", 
                    message.Topic, message.MessageId);
                throw new InvalidOperationException($"No handler registered for topic '{message.Topic}'");
            }

            // Execute the handler
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            this.logger.LogDebug(
                "Successfully processed inbox message {MessageId} with topic '{Topic}' in {ElapsedMs}ms", 
                message.MessageId, message.Topic, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, 
                "Handler failed for inbox message {MessageId}: {ErrorMessage}", 
                messageId, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task HandleFailedMessagesAsync(
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
                var message = await this.store.GetAsync(messageId, cancellationToken).ConfigureAwait(false);
                
                if (message.Attempt >= this.maxAttempts)
                {
                    this.logger.LogWarning(
                        "Inbox message {MessageId} has reached max attempts ({MaxAttempts}), marking as dead", 
                        messageId, this.maxAttempts);
                    toFail.Add(messageId);
                }
                else
                {
                    var delay = this.backoffPolicy(message.Attempt + 1);
                    this.logger.LogDebug(
                        "Inbox message {MessageId} will be retried after {DelayMs}ms delay (attempt {NextAttempt})", 
                        messageId, delay.TotalMilliseconds, message.Attempt + 1);
                    toAbandon.Add(messageId);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, 
                    "Failed to determine retry policy for message {MessageId}, abandoning for retry", 
                    messageId);
                toAbandon.Add(messageId);
            }
        }

        // Abandon messages that should be retried
        if (toAbandon.Count > 0)
        {
            await this.store.AbandonAsync(ownerToken, toAbandon, cancellationToken).ConfigureAwait(false);
        }

        // Fail messages that have exceeded max attempts
        if (toFail.Count > 0)
        {
            await this.store.FailAsync(ownerToken, toFail, "Maximum retry attempts exceeded", cancellationToken).ConfigureAwait(false);
        }
    }
}