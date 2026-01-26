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
/// Represents the result of executing an external side effect.
/// </summary>
public sealed record ExternalSideEffectExecutionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectExecutionResult"/> class.
    /// </summary>
    /// <param name="status">The execution status.</param>
    public ExternalSideEffectExecutionResult(ExternalSideEffectExecutionStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the execution status.
    /// </summary>
    public ExternalSideEffectExecutionStatus Status { get; init; }

    /// <summary>
    /// Gets the external reference identifier when known.
    /// </summary>
    public string? ExternalReferenceId { get; init; }

    /// <summary>
    /// Gets the external system status when known.
    /// </summary>
    public string? ExternalStatus { get; init; }

    /// <summary>
    /// Gets the error message when execution fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets a value indicating whether execution succeeded.
    /// </summary>
    public bool IsSuccess => Status == ExternalSideEffectExecutionStatus.Succeeded;
}
