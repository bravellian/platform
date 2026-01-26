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
/// Represents the outcome of attempting to begin an external side-effect execution.
/// </summary>
public sealed record ExternalSideEffectAttempt
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectAttempt"/> class.
    /// </summary>
    /// <param name="decision">The attempt decision.</param>
    /// <param name="record">The current record state.</param>
    /// <param name="reason">Optional reason for the decision.</param>
    public ExternalSideEffectAttempt(
        ExternalSideEffectAttemptDecision decision,
        ExternalSideEffectRecord record,
        string? reason = null)
    {
        Decision = decision;
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Reason = reason;
    }

    /// <summary>
    /// Gets the decision for the attempt.
    /// </summary>
    public ExternalSideEffectAttemptDecision Decision { get; }

    /// <summary>
    /// Gets the record associated with the attempt.
    /// </summary>
    public ExternalSideEffectRecord Record { get; }

    /// <summary>
    /// Gets the reason for the decision, when available.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets a value indicating whether execution can proceed.
    /// </summary>
    public bool CanExecute => Decision == ExternalSideEffectAttemptDecision.Ready;
}
