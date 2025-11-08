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

/*

* The outbox is a **durable intent** log. It is **transport-agnostic**.
* The dispatcher resolves a handler **by Topic** and calls it. Handlers may:
  (a) perform local work (email/report/etc.), or
  (b) forward to a broker. The dispatcher does not care.
* DB time (`SYSUTCDATETIME()`) decides if a row is **due**. The polling loop uses a **monotonic** clock for sleeps to avoid NTP/OS time jumps skewing intervals.
* Keep payloads modest; store big blobs elsewhere and pass references.
* Handlers must be **idempotent** (use DedupKey or internal unique constraints).

*/

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
/// Dispatches outbox messages to their appropriate handlers.
/// This is the core processing engine that claims messages, resolves handlers, and manages retries.
/// </summary>
internal sealed class OutboxDispatcher
{
    private readonly IOutboxStore store;
    private readonly IOutboxHandlerResolver resolver;
    private readonly Func<int, TimeSpan> backoffPolicy;
    private readonly ILogger<OutboxDispatcher> logger;

    public OutboxDispatcher(
        IOutboxStore store,
        IOutboxHandlerResolver resolver,
        ILogger<OutboxDispatcher> logger,
        Func<int, TimeSpan>? backoffPolicy = null)
    {
        this.store = store;
        this.resolver = resolver;
        this.logger = logger;
        this.backoffPolicy = backoffPolicy ?? DefaultBackoff;
    }

    /// <summary>
    /// Processes a single batch of outbox messages.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages processed.</returns>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Starting outbox batch processing with batch size {BatchSize}", batchSize);

        var messages = await this.store.ClaimDueAsync(batchSize, cancellationToken).ConfigureAwait(false);

        if (messages.Count == 0)
        {
            this.logger.LogDebug("No outbox messages available for processing");
            return 0;
        }

        this.logger.LogInformation("Processing {MessageCount} outbox messages", messages.Count);
        var processedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                await this.ProcessSingleMessageAsync(message, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.LogDebug("Outbox processing cancelled after processing {ProcessedCount} of {TotalCount} messages", processedCount, messages.Count);

                // Stop processing if cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                // Log unexpected errors but continue processing other messages
                this.logger.LogError(ex, "Unexpected error processing outbox message {MessageId} with topic '{Topic}'", message.Id, message.Topic);
            }
        }

        this.logger.LogInformation("Completed outbox batch processing: {ProcessedCount}/{TotalCount} messages processed", processedCount, messages.Count);
        return processedCount;
    }

    private async Task ProcessSingleMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            this.logger.LogDebug("Processing outbox message {MessageId} with topic '{Topic}'", message.Id, message.Topic);

            // Try to resolve handler for this topic
            if (!this.resolver.TryGet(message.Topic, out var handler))
            {
                this.logger.LogWarning("No handler registered for topic '{Topic}' - failing message {MessageId}", message.Topic, message.Id);
                await this.store.FailAsync(message.Id, $"No handler registered for topic '{message.Topic}'", cancellationToken).ConfigureAwait(false);
                SchedulerMetrics.OutboxMessagesFailed.Add(1);
                return;
            }

            // Execute the handler
            this.logger.LogDebug("Executing handler for message {MessageId} with topic '{Topic}'", message.Id, message.Topic);
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            // Mark as successfully dispatched
            this.logger.LogDebug("Successfully processed message {MessageId} with topic '{Topic}'", message.Id, message.Topic);
            await this.store.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesSent.Add(1);
        }
        catch (Exception ex)
        {
            // Handler threw an exception - reschedule with backoff
            var nextAttempt = message.RetryCount + 1;
            var delay = this.backoffPolicy(nextAttempt);

            this.logger.LogWarning(
                ex,
                "Handler failed for message {MessageId} with topic '{Topic}' (attempt {AttemptCount}). Rescheduling with {DelayMs}ms delay",
                message.Id,
                message.Topic,
                nextAttempt,
                delay.TotalMilliseconds);

            await this.store.RescheduleAsync(message.Id, delay, ex.Message, cancellationToken).ConfigureAwait(false);
            SchedulerMetrics.OutboxMessagesFailed.Add(1);
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.OutboxSendDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Default exponential backoff policy with jitter.
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
}
