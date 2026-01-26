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

/// <summary>
/// Persists external side-effect state for idempotent execution.
/// </summary>
public interface IExternalSideEffectStore
{
    /// <summary>
    /// Gets the record for a specific key.
    /// </summary>
    /// <param name="key">The external side-effect key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record if found.</returns>
    Task<ExternalSideEffectRecord?> GetAsync(ExternalSideEffectKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Gets or creates a record for the request.
    /// </summary>
    /// <param name="request">The external side-effect request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record.</returns>
    Task<ExternalSideEffectRecord> GetOrCreateAsync(ExternalSideEffectRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to begin an execution attempt for the key.
    /// </summary>
    /// <param name="key">The external side-effect key.</param>
    /// <param name="lockDuration">The lock duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt decision and record.</returns>
    Task<ExternalSideEffectAttempt> TryBeginAttemptAsync(ExternalSideEffectKey key, TimeSpan lockDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Records an external check result.
    /// </summary>
    /// <param name="key">The external side-effect key.</param>
    /// <param name="result">The check result.</param>
    /// <param name="checkedAt">Timestamp of the check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordExternalCheckAsync(
        ExternalSideEffectKey key,
        ExternalSideEffectCheckResult result,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the external side effect as succeeded.
    /// </summary>
    /// <param name="key">The external side-effect key.</param>
    /// <param name="result">The execution result.</param>
    /// <param name="completedAt">Completion timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkSucceededAsync(
        ExternalSideEffectKey key,
        ExternalSideEffectExecutionResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the external side effect as failed.
    /// </summary>
    /// <param name="key">The external side-effect key.</param>
    /// <param name="errorMessage">Failure message.</param>
    /// <param name="isPermanent">Whether the failure is permanent.</param>
    /// <param name="failedAt">Failure timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkFailedAsync(
        ExternalSideEffectKey key,
        string errorMessage,
        bool isPermanent,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}
