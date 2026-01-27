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
/// Tracks long-running operations.
/// </summary>
public interface IOperationTracker
{
    /// <summary>
    /// Starts a new operation.
    /// </summary>
    /// <param name="name">Operation name.</param>
    /// <param name="correlationContext">Optional correlation context.</param>
    /// <param name="parentOperationId">Optional parent operation identifier.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation identifier.</returns>
    Task<OperationId> StartAsync(
        string name,
        CorrelationContext? correlationContext,
        OperationId? parentOperationId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates progress for an operation.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="percentComplete">Percentage complete (0-100).</param>
    /// <param name="message">Optional progress message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateProgressAsync(
        OperationId operationId,
        double? percentComplete,
        string? message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds an append-only event to the operation log.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="kind">Event kind.</param>
    /// <param name="message">Event message.</param>
    /// <param name="dataJson">Optional JSON payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddEventAsync(
        OperationId operationId,
        string kind,
        string message,
        string? dataJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes an operation with a terminal status.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="status">Final status.</param>
    /// <param name="message">Optional completion message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(
        OperationId operationId,
        OperationStatus status,
        string? message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the current snapshot for an operation.
    /// </summary>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation snapshot.</returns>
    Task<OperationSnapshot?> GetSnapshotAsync(OperationId operationId, CancellationToken cancellationToken);
}