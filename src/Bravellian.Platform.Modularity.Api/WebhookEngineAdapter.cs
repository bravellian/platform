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
/// Adapter that connects webhook engines to HTTP-style transports.
/// </summary>
public sealed class WebhookEngineAdapter
{
    private readonly ModuleEngineDiscoveryService discovery;
    private readonly IServiceProvider services;
    private readonly IWebhookSignatureValidator signatureValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEngineAdapter"/> class.
    /// </summary>
    public WebhookEngineAdapter(ModuleEngineDiscoveryService discovery, IServiceProvider services, IWebhookSignatureValidator signatureValidator)
    {
        this.discovery = discovery;
        this.services = services;
        this.signatureValidator = signatureValidator;
    }

    /// <summary>
    /// Dispatches a webhook request to a registered engine.
    /// </summary>
    public async Task<WebhookAdapterResponse> DispatchAsync<TPayload>(WebhookAdapterRequest<TPayload> request, CancellationToken cancellationToken)
    {
        var descriptor = discovery.ResolveWebhookEngine(request.Provider, request.EventType)
            ?? throw new InvalidOperationException($"No webhook engine registered for provider '{request.Provider}' and event '{request.EventType}'.");

        if (descriptor.Manifest.Security is { } security)
        {
            if (!signatureValidator.Validate(security, request.Headers, request.RawBody, request.Signature))
            {
                return new WebhookAdapterResponse(WebhookOutcomeType.Retry, "Signature validation failed");
            }
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) && descriptor.Manifest.Security?.IdempotencyWindow is not null)
        {
            return new WebhookAdapterResponse(WebhookOutcomeType.Retry, "Missing idempotency key");
        }

        var typedDescriptor = descriptor as ModuleEngineDescriptor<IWebhookEngine<TPayload>>
            ?? throw new InvalidOperationException($"Engine '{descriptor.Manifest.Id}' does not implement expected webhook contract.");

        var engine = discovery.ResolveEngine(typedDescriptor, services)
            ?? throw new InvalidOperationException($"Engine '{descriptor.Manifest.Id}' does not implement expected webhook contract.");

        var outcome = await engine.HandleAsync(
            new WebhookRequest<TPayload>(request.Provider, request.EventType, request.Payload, request.IdempotencyKey, request.Attempt),
            cancellationToken).ConfigureAwait(false);

        return new WebhookAdapterResponse(outcome.Outcome, outcome.Reason);
    }
}
