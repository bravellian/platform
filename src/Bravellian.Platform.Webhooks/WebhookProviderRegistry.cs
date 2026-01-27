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
/// Default implementation of <see cref="IWebhookProviderRegistry"/> backed by an enumerable set of providers.
/// </summary>
public sealed class WebhookProviderRegistry : IWebhookProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IWebhookProvider> providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookProviderRegistry"/> class.
    /// </summary>
    /// <param name="providers">Registered webhook providers.</param>
    public WebhookProviderRegistry(IEnumerable<IWebhookProvider> providers)
    {
        if (providers == null)
        {
            throw new ArgumentNullException(nameof(providers));
        }

        this.providers = providers
            .Where(provider => provider != null)
            .GroupBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IWebhookProvider? Get(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        return providers.TryGetValue(providerName, out var provider) ? provider : null;
    }
}
