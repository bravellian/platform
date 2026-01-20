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
/// Result of a TryAcquire operation.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct SemaphoreAcquireResult
{
    /// <summary>
    /// Gets the status of the acquire operation.
    /// </summary>
    public required SemaphoreAcquireStatus Status { get; init; }

    /// <summary>
    /// Gets the unique token identifying this lease. Only set when Status is Acquired.
    /// </summary>
    public Guid? Token { get; init; }

    /// <summary>
    /// Gets the fencing counter for this lease. Only set when Status is Acquired.
    /// Strictly monotonically increasing per semaphore name.
    /// </summary>
    public long? Fencing { get; init; }

    /// <summary>
    /// Gets the UTC time when this lease expires. Only set when Status is Acquired.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Creates an Acquired result.
    /// </summary>
    public static SemaphoreAcquireResult Acquired(Guid token, long fencing, DateTime expiresAtUtc)
    {
        return new SemaphoreAcquireResult
        {
            Status = SemaphoreAcquireStatus.Acquired,
            Token = token,
            Fencing = fencing,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    /// <summary>
    /// Creates a NotAcquired result.
    /// </summary>
    public static SemaphoreAcquireResult NotAcquired()
    {
        return new SemaphoreAcquireResult
        {
            Status = SemaphoreAcquireStatus.NotAcquired,
        };
    }

    /// <summary>
    /// Creates an Unavailable result.
    /// </summary>
    public static SemaphoreAcquireResult Unavailable()
    {
        return new SemaphoreAcquireResult
        {
            Status = SemaphoreAcquireStatus.Unavailable,
        };
    }
}
