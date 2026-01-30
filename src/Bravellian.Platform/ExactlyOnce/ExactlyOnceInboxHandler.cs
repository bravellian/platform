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

using Bravellian.Platform.Idempotency;

namespace Bravellian.Platform.ExactlyOnce;

/// <summary>
/// Opt-in base class for exactly-once inbox handlers.
/// </summary>
public abstract class ExactlyOnceInboxHandler : IInboxHandler, IExactlyOnceKeyResolver<InboxMessage>
{
    private readonly IIdempotencyStore idempotencyStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactlyOnceInboxHandler"/> class.
    /// </summary>
    /// <param name="idempotencyStore">Idempotency store.</param>
    protected ExactlyOnceInboxHandler(IIdempotencyStore idempotencyStore)
    {
        this.idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
    }

    /// <summary>
    /// Gets the topic this handler serves.
    /// </summary>
    public abstract string Topic { get; }

    /// <summary>
    /// Resolves the stable idempotency key for this message.
    /// </summary>
    /// <param name="message">Inbox message.</param>
    /// <returns>Stable idempotency key.</returns>
    public virtual string GetKey(InboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Source))
        {
            return message.MessageId;
        }

        return string.Concat(message.Source, ":", message.MessageId);
    }

    /// <summary>
    /// Executes the inbox message and returns a structured execution result.
    /// </summary>
    /// <param name="message">Inbox message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    protected abstract Task<ExactlyOnceExecutionResult> HandleExactlyOnceAsync(
        InboxMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an optional probe for verification.
    /// </summary>
    /// <returns>Probe implementation or null.</returns>
    protected virtual IExactlyOnceProbe<InboxMessage>? ResolveProbe()
    {
        return this as IExactlyOnceProbe<InboxMessage>;
    }

    /// <inheritdoc />
    public async Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var executor = new ExactlyOnceExecutor<InboxMessage>(idempotencyStore, this, ResolveProbe());
        var result = await executor.ExecuteAsync(message, HandleExactlyOnceAsync, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == ExactlyOnceOutcome.Retry)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? ExactlyOnceDefaults.TransientFailureReason);
        }

        if (result.Outcome == ExactlyOnceOutcome.FailedPermanent)
        {
            throw new InboxPermanentFailureException(result.ErrorMessage ?? ExactlyOnceDefaults.PermanentFailureReason);
        }
    }
}
