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

namespace Bravellian.Platform.Webhooks.Tests;

public sealed class FakeSigningWebhookProvider : WebhookProviderBase
{
    public FakeSigningWebhookProvider()
        : base(new FakeAuthenticator(), new FakeClassifier(), new[] { new RecordingHandler("envelope.completed") })
    {
    }

    public override string Name => "fake";

    public RecordingHandler Handler => (RecordingHandler)Handlers[0];

    public sealed class RecordingHandler : IWebhookHandler
    {
        private readonly string eventType;
        private int invocations;

        public RecordingHandler(string eventType)
        {
            this.eventType = eventType;
        }

        public int Invocations => invocations;

        public bool CanHandle(string eventType)
        {
            return string.Equals(this.eventType, eventType, StringComparison.OrdinalIgnoreCase);
        }

        public Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticator : IWebhookAuthenticator
    {
        public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope.Headers.TryGetValue("X-Signature", out var signature)
                && string.Equals(signature, "ok", StringComparison.Ordinal))
            {
                return Task.FromResult(new AuthResult(true, null));
            }

            return Task.FromResult(new AuthResult(false, "Missing or invalid signature."));
        }
    }

    private sealed class FakeClassifier : IWebhookClassifier
    {
        public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope.BodyBytes.Length == 0)
            {
                return Task.FromResult(new ClassifyResult(
                    WebhookIngestDecision.Accepted,
                    null,
                    null,
                    WebhookDedupe.Create(envelope.Provider, null, envelope.BodyBytes).Key,
                    null,
                    null,
                    null));
            }

            try
            {
                using var document = JsonDocument.Parse(envelope.BodyBytes);
                var root = document.RootElement;
                var eventId = root.TryGetProperty("eventId", out var eventIdProp)
                    ? eventIdProp.GetString()
                    : null;
                var eventType = root.TryGetProperty("eventType", out var eventTypeProp)
                    ? eventTypeProp.GetString()
                    : null;
                var dedupeKey = string.IsNullOrWhiteSpace(eventId)
                    ? WebhookDedupe.Create(envelope.Provider, null, envelope.BodyBytes).Key
                    : WebhookDedupe.Create(envelope.Provider, eventId, envelope.BodyBytes).Key;

                return Task.FromResult(new ClassifyResult(
                    WebhookIngestDecision.Accepted,
                    eventId,
                    eventType,
                    dedupeKey,
                    null,
                    null,
                    null));
            }
            catch (JsonException)
            {
                var dedupeKey = WebhookDedupe.Create(envelope.Provider, null, envelope.BodyBytes).Key;
                return Task.FromResult(new ClassifyResult(
                    WebhookIngestDecision.Accepted,
                    null,
                    null,
                    dedupeKey,
                    null,
                    null,
                    "Invalid JSON payload."));
            }
        }
    }
}
