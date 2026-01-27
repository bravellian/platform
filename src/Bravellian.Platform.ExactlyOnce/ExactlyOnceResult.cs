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
/// Represents the final outcome of an exactly-once execution.
/// </summary>
public sealed record ExactlyOnceResult
{
    private ExactlyOnceResult(ExactlyOnceOutcome outcome, string? errorCode, string? errorMessage)
    {
        Outcome = outcome;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the final outcome.
    /// </summary>
    public ExactlyOnceOutcome Outcome { get; }

    /// <summary>
    /// Gets the optional error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the optional error message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a suppressed result.
    /// </summary>
    public static ExactlyOnceResult Suppressed(string? errorMessage = null)
    {
        return new ExactlyOnceResult(ExactlyOnceOutcome.Suppressed, null, errorMessage);
    }

    /// <summary>
    /// Creates a completed result.
    /// </summary>
    public static ExactlyOnceResult Completed()
    {
        return new ExactlyOnceResult(ExactlyOnceOutcome.Completed, null, null);
    }

    /// <summary>
    /// Creates a retry result.
    /// </summary>
    public static ExactlyOnceResult Retry(string? errorCode = null, string? errorMessage = null)
    {
        return new ExactlyOnceResult(ExactlyOnceOutcome.Retry, errorCode, errorMessage);
    }

    /// <summary>
    /// Creates a permanent failure result.
    /// </summary>
    public static ExactlyOnceResult FailedPermanent(string? errorCode = null, string? errorMessage = null)
    {
        return new ExactlyOnceResult(ExactlyOnceOutcome.FailedPermanent, errorCode, errorMessage);
    }
}
