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
/// Represents the outcome of executing an external side effect.
/// </summary>
public sealed record ExternalSideEffectOutcome
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectOutcome"/> class.
    /// </summary>
    /// <param name="status">The outcome status.</param>
    /// <param name="record">The current record state.</param>
    /// <param name="message">Optional message describing the outcome.</param>
    public ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus status, ExternalSideEffectRecord record, string? message = null)
    {
        Status = status;
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Message = message;
    }

    /// <summary>
    /// Gets the outcome status.
    /// </summary>
    public ExternalSideEffectOutcomeStatus Status { get; }

    /// <summary>
    /// Gets the current record state.
    /// </summary>
    public ExternalSideEffectRecord Record { get; }

    /// <summary>
    /// Gets the message describing the outcome.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets a value indicating whether a retry should be scheduled.
    /// </summary>
    public bool ShouldRetry => Status == ExternalSideEffectOutcomeStatus.RetryScheduled;
}
