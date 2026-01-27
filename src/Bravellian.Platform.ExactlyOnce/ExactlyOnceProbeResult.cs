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
/// Result of probing for a prior side effect.
/// </summary>
public sealed record ExactlyOnceProbeResult
{
    private ExactlyOnceProbeResult(ExactlyOnceProbeOutcome outcome, string? errorCode, string? errorMessage)
    {
        Outcome = outcome;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the probe outcome.
    /// </summary>
    public ExactlyOnceProbeOutcome Outcome { get; }

    /// <summary>
    /// Gets the optional error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the optional error message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a confirmed probe result.
    /// </summary>
    public static ExactlyOnceProbeResult Confirmed()
    {
        return new ExactlyOnceProbeResult(ExactlyOnceProbeOutcome.Confirmed, null, null);
    }

    /// <summary>
    /// Creates a not found probe result.
    /// </summary>
    public static ExactlyOnceProbeResult NotFound()
    {
        return new ExactlyOnceProbeResult(ExactlyOnceProbeOutcome.NotFound, null, null);
    }

    /// <summary>
    /// Creates an unknown probe result.
    /// </summary>
    public static ExactlyOnceProbeResult Unknown(string? errorCode = null, string? errorMessage = null)
    {
        return new ExactlyOnceProbeResult(ExactlyOnceProbeOutcome.Unknown, errorCode, errorMessage);
    }
}
