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
/// Execution result of the attempted work.
/// </summary>
public enum ExactlyOnceExecutionOutcome
{
    /// <summary>
    /// Work completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Work failed transiently and should be retried.
    /// </summary>
    TransientFailure = 1,

    /// <summary>
    /// Work failed permanently and should not be retried.
    /// </summary>
    PermanentFailure = 2
}
