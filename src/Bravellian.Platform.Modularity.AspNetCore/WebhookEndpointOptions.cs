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

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Options for mapping webhook engine endpoints.
/// </summary>
public sealed class WebhookEndpointOptions
{
    /// <summary>
    /// Route pattern for webhook intake.
    /// </summary>
    public string RoutePattern { get; set; } = "/webhooks/{provider}/{eventType}";

    /// <summary>
    /// Route parameter name for the provider.
    /// </summary>
    public string ProviderRouteParameterName { get; set; } = "provider";

    /// <summary>
    /// Route parameter name for the event type.
    /// </summary>
    public string EventTypeRouteParameterName { get; set; } = "eventType";

    /// <summary>
    /// Header name used to pass the event type into the webhook ingestion pipeline.
    /// </summary>
    public string EventTypeHeaderName { get; set; } = ModuleWebhookOptions.DefaultEventTypeHeaderName;

    // Pipeline responses are managed by Bravellian.Platform.Webhooks.AspNetCore.WebhookEndpoint.
}
