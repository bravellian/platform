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

using System.Diagnostics.CodeAnalysis;
using Bravellian.Platform.Idempotency;

namespace Bravellian.Platform.ExactlyOnce;

/// <summary>
/// Executes work with idempotency guards to achieve best-effort exactly-once semantics.
/// </summary>
/// <typeparam name="TItem">Item type.</typeparam>
public sealed class ExactlyOnceExecutor<TItem>
{
    private readonly IIdempotencyStore idempotencyStore;
    private readonly IExactlyOnceKeyResolver<TItem> keyResolver;
    private readonly IExactlyOnceProbe<TItem>? probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactlyOnceExecutor{TItem}"/> class.
    /// </summary>
    /// <param name="idempotencyStore">Idempotency store.</param>
    /// <param name="keyResolver">Key resolver.</param>
    /// <param name="probe">Optional probe.</param>
    public ExactlyOnceExecutor(
        IIdempotencyStore idempotencyStore,
        IExactlyOnceKeyResolver<TItem> keyResolver,
        IExactlyOnceProbe<TItem>? probe = null)
    {
        this.idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        this.keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
        this.probe = probe;
    }

    /// <summary>
    /// Executes work with a structured execution result.
    /// </summary>
    /// <param name="item">Item being processed.</param>
    /// <param name="execute">Execution callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Final outcome.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Executor maps execution failures to retry outcomes.")]
    public async Task<ExactlyOnceResult> ExecuteAsync(
        TItem item,
        Func<TItem, CancellationToken, Task<ExactlyOnceExecutionResult>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(execute);

        var key = ResolveKey(item);

        if (!await idempotencyStore.TryBeginAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return ExactlyOnceResult.Suppressed(ExactlyOnceDefaults.SuppressedReason);
        }

        ExactlyOnceExecutionResult executionResult;

        try
        {
            executionResult = await execute(item, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            executionResult = ExactlyOnceExecutionResult.TransientFailure(errorMessage: ex.Message);
        }

        return await FinalizeAsync(item, key, executionResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes work with exception classification.
    /// </summary>
    /// <param name="item">Item being processed.</param>
    /// <param name="execute">Execution callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="exceptionClassifier">Optional exception classifier.</param>
    /// <returns>Final outcome.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Executor maps execution failures to retry outcomes.")]
    public async Task<ExactlyOnceResult> ExecuteAsync(
        TItem item,
        Func<TItem, CancellationToken, Task> execute,
        CancellationToken cancellationToken,
        Func<Exception, ExactlyOnceExecutionResult>? exceptionClassifier = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(execute);

        var key = ResolveKey(item);

        if (!await idempotencyStore.TryBeginAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return ExactlyOnceResult.Suppressed(ExactlyOnceDefaults.SuppressedReason);
        }

        ExactlyOnceExecutionResult executionResult;

        try
        {
            await execute(item, cancellationToken).ConfigureAwait(false);
            executionResult = ExactlyOnceExecutionResult.Success();
        }
        catch (Exception ex)
        {
            executionResult = exceptionClassifier?.Invoke(ex)
                ?? ExactlyOnceExecutionResult.TransientFailure(errorMessage: ex.Message);
        }

        return await FinalizeAsync(item, key, executionResult, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExactlyOnceResult> FinalizeAsync(
        TItem item,
        string key,
        ExactlyOnceExecutionResult executionResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionResult);

        if (executionResult.Outcome == ExactlyOnceExecutionOutcome.Success)
        {
            await idempotencyStore.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
            return ExactlyOnceResult.Completed();
        }

        if (executionResult.AllowProbe && probe != null)
        {
            var probeResult = await probe.ProbeAsync(item, cancellationToken).ConfigureAwait(false);
            if (probeResult.Outcome == ExactlyOnceProbeOutcome.Confirmed)
            {
                await idempotencyStore.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
                return ExactlyOnceResult.Completed();
            }
        }

        if (executionResult.Outcome == ExactlyOnceExecutionOutcome.PermanentFailure)
        {
            await idempotencyStore.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
            return ExactlyOnceResult.FailedPermanent(executionResult.ErrorCode, executionResult.ErrorMessage);
        }

        await idempotencyStore.FailAsync(key, cancellationToken).ConfigureAwait(false);
        return ExactlyOnceResult.Retry(executionResult.ErrorCode, executionResult.ErrorMessage);
    }

    private string ResolveKey(TItem item)
    {
        var key = keyResolver.GetKey(item);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A stable idempotency key is required.", nameof(item));
        }

        return key.Trim();
    }
}
