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

using Microsoft.Extensions.DependencyInjection;

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
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new ArgumentException("Provider must be a non-empty, non-whitespace string.", nameof(request.Provider));
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            throw new ArgumentException("EventType must be a non-empty, non-whitespace string.", nameof(request.EventType));
        }

        var descriptor = discovery.ResolveWebhookEngine(request.Provider, request.EventType)
            ?? throw new InvalidOperationException($"No webhook engine registered for provider '{request.Provider}' and event '{request.EventType}'.");

        if (descriptor.Manifest.Security is { } security &&
            !signatureValidator.Validate(security, request.Headers, request.RawBody, request.Signature))
        {
            return new WebhookAdapterResponse(
                WebhookOutcomeType.Acknowledge,
                $"Signature validation failed. Expected algorithm: {security.SignatureAlgorithm}.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) && descriptor.Manifest.Security?.IdempotencyWindow is not null)
        {
            return new WebhookAdapterResponse(
                WebhookOutcomeType.Retry,
                "Idempotency key is required when an idempotency window is configured for this provider.");
        }

        ValidateRequiredServices(descriptor, request.Provider, request.EventType);

        var typedDescriptor = descriptor as ModuleEngineDescriptor<IWebhookEngine<TPayload>>
            ?? throw new InvalidOperationException($"Engine '{descriptor.Manifest.Id}' does not implement expected webhook contract.");

        var engine = discovery.ResolveEngine(typedDescriptor, services);

        var outcome = await engine.HandleAsync(
            new WebhookRequest<TPayload>(request.Provider, request.EventType, request.Payload, request.IdempotencyKey, request.Attempt),
            cancellationToken).ConfigureAwait(false);

        return new WebhookAdapterResponse(outcome.Outcome, outcome.Reason, outcome.EnqueuedEvent);
    }

    private void ValidateRequiredServices(IModuleEngineDescriptor descriptor, string provider, string eventType)
    {
        var requiredServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (descriptor.Manifest.RequiredServices is { Count: > 0 } manifestRequired)
        {
            foreach (var service in manifestRequired)
            {
                requiredServices.Add(service);
            }
        }

        if (descriptor.Manifest.WebhookMetadata is { } metadata)
        {
            foreach (var entry in metadata)
            {
                if (!string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(entry.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.RequiredServices is null)
                {
                    continue;
                }

                foreach (var service in entry.RequiredServices)
                {
                    requiredServices.Add(service);
                }
            }
        }

        if (requiredServices.Count == 0)
        {
            return;
        }

        foreach (var service in requiredServices)
        {
            if (string.IsNullOrWhiteSpace(service))
            {
                throw new InvalidOperationException(
                    $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' declares an empty required service identifier.");
            }
        }

        var validator = services.GetService<IRequiredServiceValidator>();
        if (validator is null)
        {
            throw new InvalidOperationException(
                $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' declares required services but no {nameof(IRequiredServiceValidator)} is registered.");
        }

        var missing = validator.GetMissingServices(requiredServices.ToArray()) ?? Array.Empty<string>();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' is missing required services: {string.Join(", ", missing)}.");
        }
    }
}
