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

namespace Bravellian.Platform.Email;

/// <summary>
/// Represents the outcome of a dispatch cycle.
/// </summary>
public sealed record EmailOutboxDispatchResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutboxDispatchResult"/> class.
    /// </summary>
    /// <param name="attemptedCount">Number of attempted sends.</param>
    /// <param name="succeededCount">Number of successful sends.</param>
    /// <param name="failedCount">Number of failed sends.</param>
    /// <param name="transientFailureCount">Number of transient failures.</param>
    public EmailOutboxDispatchResult(
        int attemptedCount,
        int succeededCount,
        int failedCount,
        int transientFailureCount)
    {
        AttemptedCount = attemptedCount;
        SucceededCount = succeededCount;
        FailedCount = failedCount;
        TransientFailureCount = transientFailureCount;
    }

    /// <summary>
    /// Gets the number of attempted sends.
    /// </summary>
    public int AttemptedCount { get; }

    /// <summary>
    /// Gets the number of successful sends.
    /// </summary>
    public int SucceededCount { get; }

    /// <summary>
    /// Gets the number of failed sends.
    /// </summary>
    public int FailedCount { get; }

    /// <summary>
    /// Gets the number of transient failures.
    /// </summary>
    public int TransientFailureCount { get; }
}
