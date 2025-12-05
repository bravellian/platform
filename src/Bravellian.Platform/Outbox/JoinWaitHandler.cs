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
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Handles join.wait messages to implement fan-in orchestration.
/// This handler waits for all steps in a join to complete, then executes follow-up actions.
/// </summary>
public class JoinWaitHandler : IOutboxHandler
{
    private readonly IOutboxJoinStore joinStore;
    private readonly IOutbox? outbox;
    private readonly ILogger<JoinWaitHandler> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinWaitHandler"/> class.
    /// </summary>
    public JoinWaitHandler(
        IOutboxJoinStore joinStore,
        ILogger<JoinWaitHandler> logger,
        IOutbox? outbox = null)
    {
        this.joinStore = joinStore;
        this.logger = logger;
        this.outbox = outbox;
    }

    /// <inheritdoc/>
    public string Topic => "join.wait";

    /// <inheritdoc/>
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Deserialize the payload
        var payload = JsonSerializer.Deserialize<JoinWaitPayload>(message.Payload);
        if (payload == null)
        {
            logger.LogError("Failed to deserialize join.wait payload: {Payload}", message.Payload);
            throw new InvalidOperationException("Invalid join.wait payload");
        }

        logger.LogDebug("Processing join.wait for join {JoinId}", payload.JoinId);

        // Load the join state
        var join = await joinStore.GetJoinAsync(payload.JoinId, cancellationToken).ConfigureAwait(false);
        if (join == null)
        {
            logger.LogError("Join {JoinId} not found", payload.JoinId);
            throw new InvalidOperationException($"Join {payload.JoinId} not found");
        }

        // If join is already completed or failed, this is idempotent - just exit
        if (join.Status == JoinStatus.Completed || join.Status == JoinStatus.Failed)
        {
            logger.LogDebug(
                "Join {JoinId} already in terminal status {Status}",
                payload.JoinId,
                join.Status);
            return; // Ack the message
        }

        // Check if all steps are finished
        var totalFinished = join.CompletedSteps + join.FailedSteps;
        if (totalFinished < join.ExpectedSteps)
        {
            logger.LogDebug(
                "Join {JoinId} not ready: {Completed}/{Expected} completed, {Failed} failed",
                payload.JoinId,
                join.CompletedSteps,
                join.ExpectedSteps,
                join.FailedSteps);

            // Not all steps are finished yet - abandon this message so it gets retried later
            throw new JoinNotReadyException($"Join {payload.JoinId} is not ready yet");
        }

        // All steps are finished - determine final status
        byte finalStatus;
        string? followUpTopic = null;
        string? followUpPayload = null;

        if (payload.FailIfAnyStepFailed && join.FailedSteps > 0)
        {
            // Join failed because some steps failed
            finalStatus = JoinStatus.Failed;
            followUpTopic = payload.OnFailTopic;
            followUpPayload = payload.OnFailPayload;

            logger.LogWarning(
                "Join {JoinId} failed: {Completed} completed, {Failed} failed",
                payload.JoinId,
                join.CompletedSteps,
                join.FailedSteps);
        }
        else
        {
            // Join completed successfully (or we're ignoring failures)
            finalStatus = JoinStatus.Completed;
            followUpTopic = payload.OnCompleteTopic;
            followUpPayload = payload.OnCompletePayload;

            logger.LogInformation(
                "Join {JoinId} completed: {Completed} completed, {Failed} failed",
                payload.JoinId,
                join.CompletedSteps,
                join.FailedSteps);
        }

        // Update the join status
        await joinStore.UpdateStatusAsync(
            payload.JoinId,
            finalStatus,
            cancellationToken).ConfigureAwait(false);

        // Enqueue follow-up message if configured and outbox is available
        if (!string.IsNullOrEmpty(followUpTopic) && !string.IsNullOrEmpty(followUpPayload))
        {
            if (outbox == null)
            {
                logger.LogWarning(
                    "Cannot enqueue follow-up message for join {JoinId} - IOutbox not available. Configure follow-up via alternative mechanism.",
                    payload.JoinId);
            }
            else
            {
                logger.LogDebug(
                    "Enqueueing follow-up message for join {JoinId}: topic={Topic}",
                    payload.JoinId,
                    followUpTopic);

                await outbox.EnqueueAsync(
                    followUpTopic,
                    followUpPayload,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogDebug("Successfully processed join.wait for join {JoinId}", payload.JoinId);
    }
}
