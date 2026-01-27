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
/// Describes a webhook provider with its authentication, classification, and handling capabilities.
/// </summary>
public interface IWebhookProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the authenticator used to validate incoming requests.
    /// </summary>
    IWebhookAuthenticator Authenticator { get; }

    /// <summary>
    /// Gets the classifier that decides how to handle incoming requests.
    /// </summary>
    IWebhookClassifier Classifier { get; }

    /// <summary>
    /// Gets the handlers that process classified events.
    /// </summary>
    IReadOnlyList<IWebhookHandler> Handlers { get; }
}
