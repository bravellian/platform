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

namespace Bravellian.Platform.Operations;

/// <summary>
/// Describes the lifecycle state of an operation.
/// </summary>
public enum OperationStatus
{
    /// <summary>
    /// Operation is pending execution.
    /// </summary>
    Pending,

    /// <summary>
    /// Operation is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Operation failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Operation was canceled.
    /// </summary>
    Canceled,

    /// <summary>
    /// Operation is stalled.
    /// </summary>
    Stalled
}
