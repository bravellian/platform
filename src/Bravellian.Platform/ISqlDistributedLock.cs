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

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides a mechanism for distributed locking using SQL Server.
/// </summary>
public interface ISqlDistributedLock
{
    /// <summary>
    /// Asynchronously attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="resource">A unique name for the lock resource.</param>
    /// <param name="timeout">The maximum time to wait for the lock.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains
    /// an IAsyncDisposable representing the acquired lock, or null if the lock
    /// could not be acquired within the specified timeout.
    /// </returns>
    Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
