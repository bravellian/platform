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
