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

using System.Text.Json;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Options for integrating modular webhook engines with the webhook pipeline.
/// </summary>
public sealed class ModuleWebhookOptions
{
    /// <summary>
    /// Default header used to pass event type information from endpoint routing to the classifier.
    /// </summary>
    public const string DefaultEventTypeHeaderName = "X-Bravellian-Webhook-EventType";

    /// <summary>
    /// Header name used to pass the event type into the webhook classifier.
    /// </summary>
    public string EventTypeHeaderName { get; set; } = DefaultEventTypeHeaderName;

    /// <summary>
    /// Serializer options used when deserializing webhook payloads for engines.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Optional authenticators that must succeed before webhook processing continues.
    /// </summary>
    public ICollection<Func<ModuleWebhookAuthenticatorContext, IWebhookAuthenticator>> Authenticators { get; } =
        new List<Func<ModuleWebhookAuthenticatorContext, IWebhookAuthenticator>>();
}
