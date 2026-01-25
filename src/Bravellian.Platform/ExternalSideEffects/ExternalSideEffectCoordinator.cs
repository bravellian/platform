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
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

public sealed class ExternalSideEffectCoordinator : IExternalSideEffectCoordinator
{
    private readonly IExternalSideEffectStoreProvider storeProvider;
    private readonly TimeProvider timeProvider;
    private readonly ExternalSideEffectCoordinatorOptions options;
    private readonly ILogger<ExternalSideEffectCoordinator> logger;

    public ExternalSideEffectCoordinator(
        IExternalSideEffectStoreProvider storeProvider,
        TimeProvider timeProvider,
        IOptions<ExternalSideEffectCoordinatorOptions> options,
        ILogger<ExternalSideEffectCoordinator> logger)
    {
        this.storeProvider = storeProvider;
        this.timeProvider = timeProvider;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<ExternalSideEffectOutcome> ExecuteAsync(
        ExternalSideEffectRequest request,
        Func<CancellationToken, Task<ExternalSideEffectCheckResult>>? checkAsync,
        Func<CancellationToken, Task<ExternalSideEffectExecutionResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executeAsync);

        var store = storeProvider.GetStoreByKey(request.StoreKey);
        if (store == null)
        {
            throw new InvalidOperationException($"No external side-effect store found for key '{request.StoreKey}'.");
        }

        var record = await store.GetOrCreateAsync(request, cancellationToken).ConfigureAwait(false);
        if (record.Status == ExternalSideEffectStatus.Succeeded)
        {
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.AlreadyCompleted, record);
        }

        if (record.Status == ExternalSideEffectStatus.Failed)
        {
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.PermanentFailure, record, record.LastError);
        }

        var now = timeProvider.GetUtcNow();

        if (ShouldCheckExternalState(record, checkAsync, now))
        {
            var checkResult = await checkAsync!(cancellationToken).ConfigureAwait(false)
                ?? new ExternalSideEffectCheckResult(ExternalSideEffectCheckStatus.Unknown)
                {
                    Details = "External check returned null result.",
                };
            await store.RecordExternalCheckAsync(request.Key, checkResult, now, cancellationToken).ConfigureAwait(false);

            if (checkResult.IsConfirmed)
            {
                await store.MarkSucceededAsync(request.Key, new ExternalSideEffectExecutionResult(ExternalSideEffectExecutionStatus.Succeeded)
                {
                    ExternalReferenceId = checkResult.ExternalReferenceId,
                    ExternalStatus = checkResult.ExternalStatus,
                }, now, cancellationToken).ConfigureAwait(false);

                var confirmedRecord = await store.GetAsync(request.Key, cancellationToken).ConfigureAwait(false) ?? record;
                return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.Completed, confirmedRecord, "External side effect confirmed by probe.");
            }

            if (checkResult.Status == ExternalSideEffectCheckStatus.Unknown && options.UnknownCheckBehavior == ExternalSideEffectCheckBehavior.RetryLater)
            {
                return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.RetryScheduled, record, "External side-effect check was inconclusive.");
            }
        }

        var attempt = await store.TryBeginAttemptAsync(request.Key, options.AttemptLockDuration, cancellationToken).ConfigureAwait(false);
        record = attempt.Record;

        if (attempt.Decision == ExternalSideEffectAttemptDecision.AlreadyCompleted)
        {
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.AlreadyCompleted, record);
        }

        if (attempt.Decision == ExternalSideEffectAttemptDecision.Locked)
        {
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.RetryScheduled, record, attempt.Reason ?? "External side effect is locked by another worker.");
        }

        try
        {
            var result = await executeAsync(cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                throw new InvalidOperationException("External side-effect execution returned null result.");
            }

            if (result.Status == ExternalSideEffectExecutionStatus.Succeeded)
            {
                var completedAt = timeProvider.GetUtcNow();
                await store.MarkSucceededAsync(request.Key, result, completedAt, cancellationToken).ConfigureAwait(false);
                record = await store.GetAsync(request.Key, cancellationToken).ConfigureAwait(false) ?? record;
                return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.Completed, record);
            }

            if (result.Status == ExternalSideEffectExecutionStatus.PermanentFailure)
            {
                var failedAt = timeProvider.GetUtcNow();
                await store.MarkFailedAsync(request.Key, result.ErrorMessage ?? "Permanent failure.", isPermanent: true, failedAt: failedAt, cancellationToken).ConfigureAwait(false);
                record = await store.GetAsync(request.Key, cancellationToken).ConfigureAwait(false) ?? record;
                return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.PermanentFailure, record, result.ErrorMessage);
            }

            var retryAt = timeProvider.GetUtcNow();
            await store.MarkFailedAsync(request.Key, result.ErrorMessage ?? "Retryable failure.", isPermanent: false, failedAt: retryAt, cancellationToken).ConfigureAwait(false);
            record = await store.GetAsync(request.Key, cancellationToken).ConfigureAwait(false) ?? record;
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.RetryScheduled, record, result.ErrorMessage ?? "Retryable failure.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "External side-effect execution failed for {OperationName}/{IdempotencyKey}", request.Key.OperationName, request.Key.IdempotencyKey);
            var failedAt = timeProvider.GetUtcNow();
            await store.MarkFailedAsync(request.Key, ex.Message, isPermanent: false, failedAt: failedAt, cancellationToken).ConfigureAwait(false);
            record = await store.GetAsync(request.Key, cancellationToken).ConfigureAwait(false) ?? record;
            return new ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus.RetryScheduled, record, ex.Message);
        }
    }

    private bool ShouldCheckExternalState(
        ExternalSideEffectRecord record,
        Func<CancellationToken, Task<ExternalSideEffectCheckResult>>? checkAsync,
        DateTimeOffset now)
    {
        if (checkAsync == null)
        {
            return false;
        }

        if (record.AttemptCount <= 0)
        {
            return false;
        }

        if (record.LastExternalCheckAt is DateTimeOffset lastCheck)
        {
            if (now - lastCheck < options.MinimumCheckInterval)
            {
                return false;
            }
        }

        return true;
    }
}
