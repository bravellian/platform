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

internal sealed class PostmarkEmailDeliveryWebhookHandler : IWebhookHandler
{
    private readonly IEmailDeliverySink deliverySink;
    private readonly IPlatformEventEmitter? eventEmitter;

    public PostmarkEmailDeliveryWebhookHandler(IEmailDeliverySink deliverySink, IPlatformEventEmitter? eventEmitter = null)
    {
        this.deliverySink = deliverySink ?? throw new ArgumentNullException(nameof(deliverySink));
        this.eventEmitter = eventEmitter;
    }

    public bool CanHandle(string eventType)
    {
        return string.Equals(eventType, PostmarkWebhookEventTypes.Bounce, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, PostmarkWebhookEventTypes.Suppression, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, PostmarkWebhookEventTypes.SpamComplaint, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, PostmarkWebhookEventTypes.SubscriptionChange, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!PostmarkWebhookParser.TryParse(context.BodyBytes, out var payload, out _))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.MessageKey) && string.IsNullOrWhiteSpace(payload.MessageId))
        {
            return;
        }

        var status = IsSuppressionEvent(context.EventType)
            ? EmailDeliveryStatus.Suppressed
            : EmailDeliveryStatus.Bounced;

        var update = new EmailDeliveryUpdate(
            payload.MessageKey,
            payload.MessageId,
            context.ProviderEventId,
            status,
            payload.BounceType,
            payload.Description);

        await deliverySink.RecordExternalAsync(update, cancellationToken).ConfigureAwait(false);

        EmailMetrics.RecordWebhookReceived(context.Provider, context.EventType);
        await EmailAuditEvents.EmitWebhookReceivedAsync(
            eventEmitter,
            context.Provider,
            context.EventType,
            payload.MessageKey ?? payload.MessageId,
            context.ProviderEventId,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSuppressionEvent(string? eventType)
    {
        return string.Equals(eventType, PostmarkWebhookEventTypes.Suppression, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, PostmarkWebhookEventTypes.SpamComplaint, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, PostmarkWebhookEventTypes.SubscriptionChange, StringComparison.OrdinalIgnoreCase);
    }
}
