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

using Bravellian.Platform.Email;
using Bravellian.Platform.Observability;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Postmark webhook provider implementation for delivery tracking events.
/// </summary>
public sealed class PostmarkWebhookProvider : WebhookProviderBase
{
    /// <summary>
    /// Default provider name used for Postmark webhook registration.
    /// </summary>
    public const string DefaultProviderName = "postmark";

    private readonly PostmarkWebhookOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkWebhookProvider"/> class.
    /// </summary>
    /// <param name="deliverySink">Delivery sink for recording provider updates.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    /// <param name="options">Webhook options.</param>
    public PostmarkWebhookProvider(IEmailDeliverySink deliverySink, PostmarkWebhookOptions? options = null)
        : this(deliverySink, null, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkWebhookProvider"/> class.
    /// </summary>
    /// <param name="deliverySink">Delivery sink for recording provider updates.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    /// <param name="options">Webhook options.</param>
    public PostmarkWebhookProvider(
        IEmailDeliverySink deliverySink,
        IPlatformEventEmitter? eventEmitter,
        PostmarkWebhookOptions? options = null)
        : base(
            new PostmarkWebhookAuthenticator(options ?? new PostmarkWebhookOptions()),
            new PostmarkWebhookClassifier(),
            new IWebhookHandler[] { new PostmarkEmailDeliveryWebhookHandler(deliverySink, eventEmitter) })
    {
        this.options = options ?? new PostmarkWebhookOptions();
        if (string.IsNullOrWhiteSpace(this.options.ProviderName))
        {
            this.options.ProviderName = DefaultProviderName;
        }
    }

    /// <inheritdoc />
    public override string Name => options.ProviderName;
}
