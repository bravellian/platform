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
/// Describes the result status of an external check.
/// </summary>
public enum ExternalSideEffectCheckStatus
{
    /// <summary>
    /// The external side effect is confirmed.
    /// </summary>
    Confirmed = 0,
    /// <summary>
    /// The external side effect is not found.
    /// </summary>
    NotFound = 1,
    /// <summary>
    /// The external side effect state is unknown.
    /// </summary>
    Unknown = 2,
}
