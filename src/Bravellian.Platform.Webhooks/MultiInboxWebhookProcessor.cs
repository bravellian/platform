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

using Bravellian.Platform;

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// IWebhookProcessor implementation that processes webhook inbox work across all configured stores.
/// </summary>
public sealed class MultiInboxWebhookProcessor : IWebhookProcessor
{
    private readonly IInboxWorkStoreProvider storeProvider;
    private readonly IWebhookProviderRegistry providerRegistry;
    private readonly WebhookProcessorOptions options;
    private readonly WebhookOptions webhookOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiInboxWebhookProcessor"/> class.
    /// </summary>
    /// <param name="storeProvider">Inbox work store provider.</param>
    /// <param name="providerRegistry">Webhook provider registry.</param>
    /// <param name="options">Processor options.</param>
    /// <param name="webhookOptions">Webhook options for callbacks.</param>
    public MultiInboxWebhookProcessor(
        IInboxWorkStoreProvider storeProvider,
        IWebhookProviderRegistry providerRegistry,
        WebhookProcessorOptions? options = null,
        WebhookOptions? webhookOptions = null)
    {
        this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
        this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        this.options = options ?? new WebhookProcessorOptions();
        this.webhookOptions = webhookOptions ?? new WebhookOptions();
    }

    /// <inheritdoc />
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var stores = await storeProvider.GetAllStoresAsync().ConfigureAwait(false);
        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No inbox work stores are configured. Configure at least one store or use IInboxRouter.");
        }

        var processed = 0;
        foreach (var store in stores)
        {
            var processor = new WebhookProcessor(store, providerRegistry, options, webhookOptions);
            processed += await processor.RunOnceAsync(cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }
}
