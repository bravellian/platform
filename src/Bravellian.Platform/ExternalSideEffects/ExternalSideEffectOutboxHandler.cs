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

namespace Bravellian.Platform;

/// <summary>
/// Base outbox handler for coordinating external side effects.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public abstract class ExternalSideEffectOutboxHandler<TPayload> : IOutboxHandler
{
    private static readonly Action<ILogger, string, ExternalSideEffectOutcomeStatus, string, string, Exception?> LogSideEffectCompleted =
        LoggerMessage.Define<string, ExternalSideEffectOutcomeStatus, string, string>(
            LogLevel.Debug,
            new EventId(1, "ExternalSideEffectCompleted"),
            "External side effect for topic {Topic} completed with status {Status} (operation {OperationName}, key {IdempotencyKey}).");

    private readonly IExternalSideEffectCoordinator coordinator;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectOutboxHandler{TPayload}"/> class.
    /// </summary>
    /// <param name="coordinator">External side-effect coordinator.</param>
    /// <param name="logger">Logger instance.</param>
    protected ExternalSideEffectOutboxHandler(
        IExternalSideEffectCoordinator coordinator,
        ILogger logger)
    {
        this.coordinator = coordinator;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the outbox topic handled by this handler.
    /// </summary>
    public abstract string Topic { get; }

    /// <summary>
    /// Handles an outbox message by coordinating the external side effect.
    /// </summary>
    /// <param name="message">The outbox message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = DeserializePayload(message.Payload);
        var request = CreateRequest(message, payload);
        var context = new ExternalSideEffectContext<TPayload>(message, payload, request);

        var outcome = await coordinator.ExecuteAsync(
            request,
            ct => CheckExternalAsync(context, ct),
            ct => ExecuteExternalAsync(context, ct),
            cancellationToken).ConfigureAwait(false);

        await OnOutcomeAsync(context, outcome, cancellationToken).ConfigureAwait(false);

        if (outcome.Status == ExternalSideEffectOutcomeStatus.PermanentFailure)
        {
            throw new OutboxPermanentFailureException(outcome.Message ?? "External side effect failed permanently.");
        }

        if (outcome.ShouldRetry)
        {
            throw new ExternalSideEffectRetryableException(outcome.Message ?? "External side effect requires retry.");
        }

        LogSideEffectCompleted(logger, Topic, outcome.Status, request.Key.OperationName, request.Key.IdempotencyKey, null);
    }

    /// <summary>
    /// Deserializes the payload from the outbox message.
    /// </summary>
    /// <param name="payload">Serialized payload.</param>
    /// <returns>The deserialized payload.</returns>
    protected abstract TPayload DeserializePayload(string payload);

    /// <summary>
    /// Creates the external side-effect request for the payload.
    /// </summary>
    /// <param name="message">The outbox message.</param>
    /// <param name="payload">The deserialized payload.</param>
    /// <returns>The external side-effect request.</returns>
    protected abstract ExternalSideEffectRequest CreateRequest(OutboxMessage message, TPayload payload);

    /// <summary>
    /// Checks external state before execution.
    /// </summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The check result.</returns>
    protected virtual Task<ExternalSideEffectCheckResult> CheckExternalAsync(
        ExternalSideEffectContext<TPayload> context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new ExternalSideEffectCheckResult(ExternalSideEffectCheckStatus.Unknown));
    }

    /// <summary>
    /// Executes the external side effect.
    /// </summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result.</returns>
    protected abstract Task<ExternalSideEffectExecutionResult> ExecuteExternalAsync(
        ExternalSideEffectContext<TPayload> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles the outcome after execution.
    /// </summary>
    /// <param name="context">Execution context.</param>
    /// <param name="outcome">Execution outcome.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the outcome handling.</returns>
    protected virtual Task OnOutcomeAsync(
        ExternalSideEffectContext<TPayload> context,
        ExternalSideEffectOutcome outcome,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = outcome;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
