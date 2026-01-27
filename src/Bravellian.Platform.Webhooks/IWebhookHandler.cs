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
/// Handles classified webhook events.
/// </summary>
public interface IWebhookHandler
{
    /// <summary>
    /// Determines whether this handler can process the event type.
    /// </summary>
    /// <param name="eventType">Event type identifier.</param>
    /// <returns><c>true</c> if the handler can process the event.</returns>
    bool CanHandle(string eventType);

    /// <summary>
    /// Handles the webhook event.
    /// </summary>
    /// <param name="context">Webhook event context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken);
}
