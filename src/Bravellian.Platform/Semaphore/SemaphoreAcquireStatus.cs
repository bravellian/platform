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

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// Result of a semaphore acquire operation.
/// </summary>
public enum SemaphoreAcquireStatus
{
    /// <summary>
    /// The semaphore lease was successfully acquired.
    /// </summary>
    Acquired,

    /// <summary>
    /// The semaphore is at capacity; no lease was acquired.
    /// </summary>
    NotAcquired,

    /// <summary>
    /// The control plane is unavailable; operation could not be completed.
    /// </summary>
    Unavailable,
}
