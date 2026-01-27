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

namespace Bravellian.Platform.Email;

/// <summary>
/// Default implementation of <see cref="IEmailOutboxDispatcher"/>.
/// </summary>
public sealed class EmailOutboxDispatcher : IEmailOutboxDispatcher
{
    private readonly IEmailOutboxStore outboxStore;
    private readonly IOutboundEmailSender sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutboxDispatcher"/> class.
    /// </summary>
    /// <param name="outboxStore">Outbox store.</param>
    /// <param name="sender">Outbound email sender.</param>
    public EmailOutboxDispatcher(IEmailOutboxStore outboxStore, IOutboundEmailSender sender)
    {
        this.outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    /// <inheritdoc />
    public async Task<EmailOutboxDispatchResult> DispatchAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "Batch size must be greater than zero.");
        }

        var items = await outboxStore.DequeueAsync(maxBatchSize, cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return new EmailOutboxDispatchResult(0, 0, 0, 0);
        }

        var succeeded = 0;
        var failed = 0;
        var transientFailures = 0;

        foreach (var item in items)
        {
            var result = await sender.SendAsync(item.Message, cancellationToken).ConfigureAwait(false);

            if (result.Status == EmailDeliveryStatus.Sent)
            {
                succeeded++;
                await outboxStore.MarkSucceededAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                failed++;
                if (result.FailureType == EmailFailureType.Transient)
                {
                    transientFailures++;
                }

                await outboxStore.MarkFailedAsync(item.Id, result.ErrorMessage, cancellationToken).ConfigureAwait(false);
            }
        }

        return new EmailOutboxDispatchResult(items.Count, succeeded, failed, transientFailures);
    }
}
