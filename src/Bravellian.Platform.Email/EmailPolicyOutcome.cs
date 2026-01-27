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
/// Represents the outcome of an email send policy evaluation.
/// </summary>
public enum EmailPolicyOutcome
{
    /// <summary>
    /// Allow the send to proceed.
    /// </summary>
    Allow = 0,

    /// <summary>
    /// Delay the send until later.
    /// </summary>
    Delay = 1,

    /// <summary>
    /// Reject the send permanently.
    /// </summary>
    Reject = 2
}
