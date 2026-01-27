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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Base class for composing webhook providers from their parts.
/// </summary>
public abstract class WebhookProviderBase : IWebhookProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookProviderBase"/> class.
    /// </summary>
    /// <param name="authenticator">Authenticator implementation.</param>
    /// <param name="classifier">Classifier implementation.</param>
    /// <param name="handlers">Handlers for processed events.</param>
    protected WebhookProviderBase(
        IWebhookAuthenticator authenticator,
        IWebhookClassifier classifier,
        IReadOnlyCollection<IWebhookHandler> handlers)
    {
        Authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        Classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public IWebhookAuthenticator Authenticator { get; }

    /// <inheritdoc />
    public IWebhookClassifier Classifier { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<IWebhookHandler> Handlers { get; }
}
