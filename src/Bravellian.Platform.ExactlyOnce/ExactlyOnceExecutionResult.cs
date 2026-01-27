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

namespace Bravellian.Platform.ExactlyOnce;

/// <summary>
/// Represents the outcome of a single execution attempt.
/// </summary>
public sealed record ExactlyOnceExecutionResult
{
    private ExactlyOnceExecutionResult(
        ExactlyOnceExecutionOutcome outcome,
        string? errorCode,
        string? errorMessage,
        bool allowProbe)
    {
        Outcome = outcome;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        AllowProbe = allowProbe;
    }

    /// <summary>
    /// Gets the execution outcome.
    /// </summary>
    public ExactlyOnceExecutionOutcome Outcome { get; }

    /// <summary>
    /// Gets the optional error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the optional error message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets a value indicating whether a probe is allowed for this outcome.
    /// </summary>
    public bool AllowProbe { get; }

    /// <summary>
    /// Returns a successful execution result.
    /// </summary>
    public static ExactlyOnceExecutionResult Success()
    {
        return new ExactlyOnceExecutionResult(ExactlyOnceExecutionOutcome.Success, null, null, allowProbe: false);
    }

    /// <summary>
    /// Returns a transient failure execution result.
    /// </summary>
    public static ExactlyOnceExecutionResult TransientFailure(
        string? errorCode = null,
        string? errorMessage = null,
        bool allowProbe = true)
    {
        return new ExactlyOnceExecutionResult(ExactlyOnceExecutionOutcome.TransientFailure, errorCode, errorMessage, allowProbe);
    }

    /// <summary>
    /// Returns a permanent failure execution result.
    /// </summary>
    public static ExactlyOnceExecutionResult PermanentFailure(
        string? errorCode = null,
        string? errorMessage = null,
        bool allowProbe = false)
    {
        return new ExactlyOnceExecutionResult(ExactlyOnceExecutionOutcome.PermanentFailure, errorCode, errorMessage, allowProbe);
    }
}
