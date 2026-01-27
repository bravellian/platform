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
/// Final outcome of exactly-once execution.
/// </summary>
public enum ExactlyOnceOutcome
{
    /// <summary>
    /// Execution was suppressed due to an existing idempotency key.
    /// </summary>
    Suppressed = 0,

    /// <summary>
    /// Execution completed successfully.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Execution should be retried later.
    /// </summary>
    Retry = 2,

    /// <summary>
    /// Execution failed permanently.
    /// </summary>
    FailedPermanent = 3
}
