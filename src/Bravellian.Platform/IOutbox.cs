namespace Bravellian.Platform;
using System.Data;
using System.Threading.Tasks;

/// <summary>
/// Provides a mechanism to enqueue messages for later processing
/// as part of a transactional operation.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueues a message into the outbox table within the context
    /// of an existing database transaction.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null);
}