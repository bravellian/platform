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

namespace Bravellian.Platform.Operations;

/// <summary>
/// Provides helper methods for operation tracking.
/// </summary>
public static class OperationTrackerExtensions
{
    /// <summary>
    /// Records a failure event and completes the operation as failed.
    /// </summary>
    /// <param name="tracker">Operation tracker.</param>
    /// <param name="operationId">Operation identifier.</param>
    /// <param name="exception">Exception that caused the failure.</param>
    /// <param name="message">Optional failure message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RecordFailureAsync(
        this IOperationTracker tracker,
        OperationId operationId,
        Exception exception,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        if (tracker is null)
        {
            throw new ArgumentNullException(nameof(tracker));
        }

        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var failureMessage = string.IsNullOrWhiteSpace(message) ? exception.Message : message;

        await tracker.AddEventAsync(
            operationId,
            "Error",
            failureMessage,
            exception.ToString(),
            cancellationToken).ConfigureAwait(false);

        await tracker.CompleteAsync(operationId, OperationStatus.Failed, failureMessage, cancellationToken)
            .ConfigureAwait(false);
    }
}