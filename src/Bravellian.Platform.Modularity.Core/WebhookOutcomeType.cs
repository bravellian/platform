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
/// Webhook outcome types understood by adapters.
/// </summary>
public enum WebhookOutcomeType
{
    /// <summary>
    /// Acknowledge the webhook and stop retries.
    /// </summary>
    Acknowledge,

    /// <summary>
    /// Ask the transport to retry the webhook.
    /// </summary>
    Retry,

    /// <summary>
    /// Enqueue an event internally while acknowledging the webhook.
    /// </summary>
    EnqueueEvent,
}
