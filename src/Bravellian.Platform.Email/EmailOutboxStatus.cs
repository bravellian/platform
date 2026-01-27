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
/// Represents the state of an outbox record.
/// </summary>
public enum EmailOutboxStatus
{
    /// <summary>
    /// Pending dispatch.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Currently processing.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Successfully dispatched.
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// Failed to dispatch.
    /// </summary>
    Failed = 3
}
