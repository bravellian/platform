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
/// Describes the outcome status of an external side effect.
/// </summary>
public enum ExternalSideEffectOutcomeStatus
{
    /// <summary>
    /// The external side effect completed successfully.
    /// </summary>
    Completed = 0,
    /// <summary>
    /// The external side effect was already completed.
    /// </summary>
    AlreadyCompleted = 1,
    /// <summary>
    /// A retry has been scheduled.
    /// </summary>
    RetryScheduled = 2,
    /// <summary>
    /// The external side effect failed permanently.
    /// </summary>
    PermanentFailure = 3,
}
