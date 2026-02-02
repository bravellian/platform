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

using Bravellian.Platform.ExactlyOnce;
using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Observability;

namespace Bravellian.Platform.Email;

/// <summary>
/// Processes outbound email messages across all configured outbox stores.
/// </summary>
public sealed class MultiEmailOutboxProcessor : IEmailOutboxProcessor
{
    private readonly IOutboxStoreProvider storeProvider;
    private readonly IOutboundEmailSender sender;
    private readonly IIdempotencyStore idempotencyStore;
    private readonly IEmailDeliverySink deliverySink;
    private readonly IOutboundEmailProbe? probe;
    private readonly IPlatformEventEmitter? eventEmitter;
    private readonly IEmailSendPolicy? policy;
    private readonly TimeProvider? timeProvider;
    private readonly EmailOutboxProcessorOptions? options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiEmailOutboxProcessor"/> class.
    /// </summary>
    /// <param name="storeProvider">Outbox store provider.</param>
    /// <param name="sender">Outbound email sender.</param>
    /// <param name="idempotencyStore">Idempotency store.</param>
    /// <param name="deliverySink">Delivery sink.</param>
    /// <param name="probe">Optional outbound probe.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    /// <param name="policy">Optional send policy.</param>
    /// <param name="timeProvider">Optional time provider.</param>
    /// <param name="options">Optional processor options.</param>
    public MultiEmailOutboxProcessor(
        IOutboxStoreProvider storeProvider,
        IOutboundEmailSender sender,
        IIdempotencyStore idempotencyStore,
        IEmailDeliverySink deliverySink,
        IOutboundEmailProbe? probe = null,
        IPlatformEventEmitter? eventEmitter = null,
        IEmailSendPolicy? policy = null,
        TimeProvider? timeProvider = null,
        EmailOutboxProcessorOptions? options = null)
    {
        this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        this.idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        this.deliverySink = deliverySink ?? throw new ArgumentNullException(nameof(deliverySink));
        this.probe = probe;
        this.eventEmitter = eventEmitter;
        this.policy = policy;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <inheritdoc />
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var stores = await storeProvider.GetAllStoresAsync().ConfigureAwait(false);
        if (stores.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        foreach (var store in stores)
        {
            var processor = new EmailOutboxProcessor(
                store,
                sender,
                idempotencyStore,
                deliverySink,
                probe,
                eventEmitter,
                policy,
                timeProvider,
                options);

            processed += await processor.ProcessOnceAsync(cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }
}
