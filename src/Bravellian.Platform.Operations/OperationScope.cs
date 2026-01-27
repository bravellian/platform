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

using Bravellian.Platform.Correlation;

namespace Bravellian.Platform.Operations;

/// <summary>
/// Provides a scope that starts and completes an operation.
/// </summary>
public sealed class OperationScope : IDisposable, IAsyncDisposable
{
    private readonly IOperationTracker tracker;
    private readonly OperationId operationId;
    private readonly string? successMessage;
    private readonly CancellationToken cancellationToken;
    private bool completed;
    private Exception? exception;
    private string? failureMessage;

    private OperationScope(
        IOperationTracker tracker,
        OperationId operationId,
        string? successMessage,
        CancellationToken cancellationToken)
    {
        this.tracker = tracker;
        this.operationId = operationId;
        this.successMessage = successMessage;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the operation identifier.
    /// </summary>
    public OperationId OperationId => operationId;

    /// <summary>
    /// Starts a new operation and returns a scope that completes it on disposal.
    /// </summary>
    /// <param name="tracker">Operation tracker.</param>
    /// <param name="name">Operation name.</param>
    /// <param name="correlationContext">Optional correlation context.</param>
    /// <param name="parentOperationId">Optional parent operation identifier.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="successMessage">Optional success message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation scope.</returns>
    public static async Task<OperationScope> StartAsync(
        IOperationTracker tracker,
        string name,
        CorrelationContext? correlationContext = null,
        OperationId? parentOperationId = null,
        IReadOnlyDictionary<string, string>? tags = null,
        string? successMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (tracker is null)
        {
            ArgumentNullException.ThrowIfNull(tracker);
        }

        var trackedOperationId = await tracker.StartAsync(
            name,
            correlationContext,
            parentOperationId,
            tags,
            cancellationToken).ConfigureAwait(false);

        return new OperationScope(tracker, trackedOperationId, successMessage, cancellationToken);
    }

    /// <summary>
    /// Executes an operation and completes it based on success or failure.
    /// </summary>
    /// <param name="tracker">Operation tracker.</param>
    /// <param name="name">Operation name.</param>
    /// <param name="action">Action to execute.</param>
    /// <param name="correlationContext">Optional correlation context.</param>
    /// <param name="parentOperationId">Optional parent operation identifier.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="successMessage">Optional success message.</param>
    /// <param name="failureMessage">Optional failure message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation identifier.</returns>
    public static async Task<OperationId> RunAsync(
        IOperationTracker tracker,
        string name,
        Func<CancellationToken, Task> action,
        CorrelationContext? correlationContext = null,
        OperationId? parentOperationId = null,
        IReadOnlyDictionary<string, string>? tags = null,
        string? successMessage = null,
        string? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (action is null)
        {
            ArgumentNullException.ThrowIfNull(action);
        }

        var scope = await StartAsync(
            tracker,
            name,
            correlationContext,
            parentOperationId,
            tags,
            successMessage,
            cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                await scope.CompleteAsync(OperationStatus.Succeeded, successMessage, cancellationToken)
                    .ConfigureAwait(false);
                return scope.OperationId;
            }
            catch (Exception ex)
            {
                scope.Fail(ex, failureMessage);
                throw;
            }
        }
    }

    /// <summary>
    /// Marks the scope as failed when disposed.
    /// </summary>
    /// <param name="exception">Exception that caused the failure.</param>
    /// <param name="message">Optional failure message.</param>
    public void Fail(Exception exception, string? message = null)
    {
        if (exception is null)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        this.exception = exception;
        failureMessage = message;
    }

    /// <summary>
    /// Completes the operation explicitly.
    /// </summary>
    /// <param name="status">Completion status.</param>
    /// <param name="message">Optional message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CompleteAsync(OperationStatus status, string? message, CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            return;
        }

        await tracker.CompleteAsync(operationId, status, message, cancellationToken).ConfigureAwait(false);
        completed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (completed)
        {
            return;
        }

        if (exception is not null)
        {
            await tracker.RecordFailureAsync(operationId, exception, failureMessage, cancellationToken)
                .ConfigureAwait(false);
            completed = true;
            return;
        }

        await tracker.CompleteAsync(operationId, OperationStatus.Succeeded, successMessage, cancellationToken)
            .ConfigureAwait(false);
        completed = true;
    }
}
