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

using System.Diagnostics.CodeAnalysis;

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Registry for webhook providers.
/// </summary>
public interface IWebhookProviderRegistry
{
    /// <summary>
    /// Gets a provider by name.
    /// </summary>
    /// <param name="providerName">Provider name.</param>
    /// <returns>The provider, or <c>null</c> when not found.</returns>
    [SuppressMessage("Design", "CA1716:Identifiers should not match keywords", Justification = "Common registry pattern uses Get for lookup.")]
    IWebhookProvider? Get(string providerName);
}
