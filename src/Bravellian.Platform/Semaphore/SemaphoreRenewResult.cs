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

using System.Runtime.InteropServices;

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// Result of a Renew operation.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Result type does not require custom equality semantics.")]
public readonly struct SemaphoreRenewResult
{
    /// <summary>
    /// Gets the status of the renew operation.
    /// </summary>
    public required SemaphoreRenewStatus Status { get; init; }

    /// <summary>
    /// Gets the new expiry time if renewed successfully.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Creates a Renewed result.
    /// </summary>
    public static SemaphoreRenewResult Renewed(DateTime expiresAtUtc)
    {
        return new SemaphoreRenewResult
        {
            Status = SemaphoreRenewStatus.Renewed,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    /// <summary>
    /// Creates a Lost result.
    /// </summary>
    public static SemaphoreRenewResult Lost()
    {
        return new SemaphoreRenewResult
        {
            Status = SemaphoreRenewStatus.Lost,
        };
    }

    /// <summary>
    /// Creates an Unavailable result.
    /// </summary>
    public static SemaphoreRenewResult Unavailable()
    {
        return new SemaphoreRenewResult
        {
            Status = SemaphoreRenewStatus.Unavailable,
        };
    }
}
