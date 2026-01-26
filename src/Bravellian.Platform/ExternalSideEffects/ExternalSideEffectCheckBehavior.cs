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
/// Specifies how to handle unknown external check results.
/// </summary>
public enum ExternalSideEffectCheckBehavior
{
    /// <summary>
    /// Schedule a retry when the check is inconclusive.
    /// </summary>
    RetryLater = 0,
    /// <summary>
    /// Continue execution even when the check is inconclusive.
    /// </summary>
    ExecuteAnyway = 1,
}
