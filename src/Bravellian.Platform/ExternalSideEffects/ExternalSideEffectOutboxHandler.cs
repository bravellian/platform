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

public abstract class ExternalSideEffectOutboxHandler<TPayload> : IOutboxHandler
{
    private readonly IExternalSideEffectCoordinator coordinator;
    private readonly ILogger logger;

    protected ExternalSideEffectOutboxHandler(
        IExternalSideEffectCoordinator coordinator,
        ILogger logger)
    {
        this.coordinator = coordinator;
        this.logger = logger;
    }

    public abstract string Topic { get; }

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

        if (outcome.Status == ExternalSideEffectOutcomeStatus.PermanentFailure)
        {
            throw new OutboxPermanentFailureException(outcome.Message ?? "External side effect failed permanently.");
        }

        if (outcome.ShouldRetry)
        {
            throw new ExternalSideEffectRetryableException(outcome.Message ?? "External side effect requires retry.");
        }

        logger.LogDebug(
            "External side effect for topic {Topic} completed with status {Status} (operation {OperationName}, key {IdempotencyKey}).",
            Topic,
            outcome.Status,
            request.Key.OperationName,
            request.Key.IdempotencyKey);
    }

    protected abstract TPayload DeserializePayload(string payload);

    protected abstract ExternalSideEffectRequest CreateRequest(OutboxMessage message, TPayload payload);

    protected virtual Task<ExternalSideEffectCheckResult> CheckExternalAsync(
        ExternalSideEffectContext<TPayload> context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new ExternalSideEffectCheckResult(ExternalSideEffectCheckStatus.Unknown));
    }

    protected abstract Task<ExternalSideEffectExecutionResult> ExecuteExternalAsync(
        ExternalSideEffectContext<TPayload> context,
        CancellationToken cancellationToken);
}
