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
/// Webhook outcome mapped to transport responses by adapters.
/// </summary>
/// <param name="Outcome">Outcome type.</param>
/// <param name="Reason">Optional reason for retries or re-queues.</param>
/// <param name="EnqueuedEvent">Optional event payload to enqueue downstream.</param>
public sealed record WebhookOutcome(WebhookOutcomeType Outcome, string? Reason = null, object? EnqueuedEvent = null)
{
    /// <summary>
    /// Creates an acknowledge outcome.
    /// </summary>
    public static WebhookOutcome Acknowledge() => new(WebhookOutcomeType.Acknowledge);

    /// <summary>
    /// Creates a retry outcome.
    /// </summary>
    public static WebhookOutcome Retry(string reason) => new(WebhookOutcomeType.Retry, reason);

    /// <summary>
    /// Creates an enqueue outcome.
    /// </summary>
    public static WebhookOutcome Enqueue(object enqueued) => new(WebhookOutcomeType.EnqueueEvent, null, enqueued);
}
