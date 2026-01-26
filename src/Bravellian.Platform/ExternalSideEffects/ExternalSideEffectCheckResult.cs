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
/// Represents the result of an external side-effect check.
/// </summary>
public sealed record ExternalSideEffectCheckResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectCheckResult"/> class.
    /// </summary>
    /// <param name="status">The check status.</param>
    public ExternalSideEffectCheckResult(ExternalSideEffectCheckStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the check status.
    /// </summary>
    public ExternalSideEffectCheckStatus Status { get; init; }

    /// <summary>
    /// Gets the external reference identifier when known.
    /// </summary>
    public string? ExternalReferenceId { get; init; }

    /// <summary>
    /// Gets the external system status when known.
    /// </summary>
    public string? ExternalStatus { get; init; }

    /// <summary>
    /// Gets additional details about the check.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets a value indicating whether the external state is confirmed.
    /// </summary>
    public bool IsConfirmed => Status == ExternalSideEffectCheckStatus.Confirmed;
}
