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
/// Defines the possible status values for outbox joins in the fan-in pattern.
/// </summary>
internal static class JoinStatus
{
    /// <summary>
    /// Join is waiting for steps to complete.
    /// </summary>
    public const byte Pending = 0;

    /// <summary>
    /// All steps have completed successfully.
    /// </summary>
    public const byte Completed = 1;

    /// <summary>
    /// One or more steps have failed, causing the join to fail.
    /// </summary>
    public const byte Failed = 2;

    /// <summary>
    /// Join has been cancelled.
    /// </summary>
    public const byte Cancelled = 3;
}
