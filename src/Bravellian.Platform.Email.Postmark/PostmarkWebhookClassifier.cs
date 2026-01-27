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

using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Email.Postmark;

internal sealed class PostmarkWebhookClassifier : IWebhookClassifier
{
    public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.BodyBytes.Length == 0)
        {
            return Task.FromResult(new ClassifyResult(
                WebhookIngestDecision.Rejected,
                null,
                null,
                null,
                null,
                null,
                "Postmark webhook payload is empty."));
        }

        if (!PostmarkWebhookParser.TryParse(envelope.BodyBytes, out var payload, out var error))
        {
            return Task.FromResult(new ClassifyResult(
                WebhookIngestDecision.Rejected,
                null,
                null,
                null,
                null,
                null,
                error ?? "Postmark webhook payload could not be parsed."));
        }

        var eventType = PostmarkWebhookEventTypes.Map(payload.RecordType);
        var providerEventId = payload.EventId;
        var dedupeKey = WebhookDedupe.Create(envelope.Provider, providerEventId, envelope.BodyBytes).Key;

        return Task.FromResult(new ClassifyResult(
            WebhookIngestDecision.Accepted,
            providerEventId,
            eventType,
            dedupeKey,
            null,
            null,
            null));
    }
}
