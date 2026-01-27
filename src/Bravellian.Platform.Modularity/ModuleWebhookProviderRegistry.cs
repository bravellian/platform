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

using System.Collections.Concurrent;
using System.Text.Json;
using Bravellian.Platform.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Webhook provider registry that exposes module webhook engines through the webhook pipeline.
/// </summary>
public sealed class ModuleWebhookProviderRegistry : IWebhookProviderRegistry
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ModuleEngineDiscoveryService discovery;
    private readonly IServiceProvider services;
    private readonly ModuleWebhookOptions options;
    private readonly ConcurrentDictionary<string, IWebhookProvider?> providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleWebhookProviderRegistry"/> class.
    /// </summary>
    public ModuleWebhookProviderRegistry(ModuleEngineDiscoveryService discovery, IServiceProvider services, ModuleWebhookOptions? options = null)
    {
        this.discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.options = options ?? new ModuleWebhookOptions();
    }

    /// <inheritdoc />
    public IWebhookProvider? Get(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        return providers.GetOrAdd(providerName, BuildProvider);
    }

    private IWebhookProvider? BuildProvider(string providerName)
    {
        var entries = discovery.List(EngineKind.Webhook)
            .SelectMany(descriptor => ExpandWebhookMetadata(descriptor))
            .Where(entry => string.Equals(entry.Metadata.Provider, providerName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
        {
            return null;
        }

        var byEvent = entries
            .GroupBy(entry => entry.Metadata.EventType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => BuildEventDefinition(providerName, group),
                StringComparer.OrdinalIgnoreCase);

        return new ModuleWebhookProvider(providerName, byEvent, discovery, services, options);
    }

    private static IEnumerable<WebhookMetadataEntry> ExpandWebhookMetadata(IModuleEngineDescriptor descriptor)
    {
        if (descriptor.Manifest.WebhookMetadata is null)
        {
            return Enumerable.Empty<WebhookMetadataEntry>();
        }

        return descriptor.Manifest.WebhookMetadata
            .Where(metadata => !string.IsNullOrWhiteSpace(metadata.Provider)
                               && !string.IsNullOrWhiteSpace(metadata.EventType))
            .Select(metadata => new WebhookMetadataEntry(descriptor, metadata));
    }

    private static ModuleWebhookEventDefinition BuildEventDefinition(
        string providerName,
        IGrouping<string, WebhookMetadataEntry> group)
    {
        ModuleEngineSecurity? security = null;
        var bindings = new List<ModuleWebhookEngineBinding>();

        foreach (var entry in group)
        {
            var manifestSecurity = entry.Descriptor.Manifest.Security;
            if (security is null)
            {
                security = manifestSecurity;
            }
            else if (manifestSecurity is not null && !Equals(security, manifestSecurity))
            {
                throw new InvalidOperationException(
                    $"Webhook provider '{providerName}' event '{group.Key}' declares multiple security configurations. Align security settings before enabling fanout.");
            }

            bindings.Add(new ModuleWebhookEngineBinding(
                entry.Descriptor,
                entry.Metadata,
                entry.Metadata.PayloadSchema.ClrType));
        }

        return new ModuleWebhookEventDefinition(group.Key, security, bindings);
    }

    private sealed class ModuleWebhookProvider : IWebhookProvider
    {
        private readonly ModuleEngineDiscoveryService discovery;
        private readonly IServiceProvider services;
        private readonly ModuleWebhookOptions options;
        private readonly IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events;
        private readonly IReadOnlyList<IWebhookHandler> handlers;

        public ModuleWebhookProvider(
            string name,
            IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events,
            ModuleEngineDiscoveryService discovery,
            IServiceProvider services,
            ModuleWebhookOptions options)
        {
            Name = name;
            this.events = events;
            this.discovery = discovery;
            this.services = services;
            this.options = options;
            Authenticator = BuildAuthenticator(name, services, options, events);
            Classifier = new ModuleWebhookClassifier(events, options);
            handlers = new[] { new ModuleWebhookHandler(name, events, discovery, services, options) };
        }

        public string Name { get; }

        public IWebhookAuthenticator Authenticator { get; }

        public IWebhookClassifier Classifier { get; }

        public IReadOnlyList<IWebhookHandler> Handlers => handlers;
    }

    private sealed class ModuleWebhookAuthenticator : IWebhookAuthenticator
    {
        private readonly IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events;
        private readonly ModuleWebhookOptions options;

        public ModuleWebhookAuthenticator(
            IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events,
            ModuleWebhookOptions options)
        {
            this.events = events;
            this.options = options;
        }

        public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!TryGetHeaderValue(envelope.Headers, options.EventTypeHeaderName, out var eventType)
                || string.IsNullOrWhiteSpace(eventType))
            {
                return Task.FromResult(new AuthResult(false, "Webhook event type header is missing."));
            }

            if (!events.TryGetValue(eventType, out var definition))
            {
                return Task.FromResult(new AuthResult(false, $"Webhook event type '{eventType}' is not registered."));
            }

            return Task.FromResult(new AuthResult(true, null));
        }
    }

    private sealed class CompositeWebhookAuthenticator : IWebhookAuthenticator
    {
        private readonly IReadOnlyList<IWebhookAuthenticator> authenticators;

        public CompositeWebhookAuthenticator(IReadOnlyList<IWebhookAuthenticator> authenticators)
        {
            this.authenticators = authenticators;
        }

        public async Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            foreach (var authenticator in authenticators)
            {
                var result = await authenticator.AuthenticateAsync(envelope, cancellationToken).ConfigureAwait(false);
                if (!result.IsAuthenticated)
                {
                    return result;
                }
            }

            return new AuthResult(true, null);
        }
    }

    private sealed class ModuleWebhookClassifier : IWebhookClassifier
    {
        private readonly IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events;
        private readonly ModuleWebhookOptions options;

        public ModuleWebhookClassifier(
            IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events,
            ModuleWebhookOptions options)
        {
            this.events = events;
            this.options = options;
        }

        public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!TryGetHeaderValue(envelope.Headers, options.EventTypeHeaderName, out var eventType)
                || string.IsNullOrWhiteSpace(eventType))
            {
                return Task.FromResult(new ClassifyResult(
                    WebhookIngestDecision.Rejected,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Webhook event type header is missing."));
            }

            if (!events.ContainsKey(eventType))
            {
                return Task.FromResult(new ClassifyResult(
                    WebhookIngestDecision.Rejected,
                    null,
                    eventType,
                    null,
                    null,
                    null,
                    $"Webhook event type '{eventType}' is not registered."));
            }

            return Task.FromResult(new ClassifyResult(
                WebhookIngestDecision.Accepted,
                null,
                eventType,
                null,
                null,
                null,
                null));
        }
    }

    private sealed class ModuleWebhookHandler : IWebhookHandler
    {
        private readonly string providerName;
        private readonly IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events;
        private readonly ModuleEngineDiscoveryService discovery;
        private readonly IServiceProvider services;
        private readonly ModuleWebhookOptions options;

        public ModuleWebhookHandler(
            string providerName,
            IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events,
            ModuleEngineDiscoveryService discovery,
            IServiceProvider services,
            ModuleWebhookOptions options)
        {
            this.providerName = providerName;
            this.events = events;
            this.discovery = discovery;
            this.services = services;
            this.options = options;
        }

        public bool CanHandle(string eventType) => events.ContainsKey(eventType);

        public async Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(context.EventType)
                || !events.TryGetValue(context.EventType, out var definition))
            {
                return;
            }

            foreach (var binding in definition.Bindings)
            {
                ValidateRequiredServices(binding);

                object? payload;
                try
                {
                    payload = JsonSerializer.Deserialize(
                        context.BodyBytes,
                        binding.PayloadType,
                        options.SerializerOptions ?? DefaultSerializerOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (payload is null)
                {
                    continue;
                }

                var engine = discovery.ResolveEngine(binding.Descriptor, services);
                await InvokeEngineAsync(engine, binding.PayloadType, context, payload, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private void ValidateRequiredServices(ModuleWebhookEngineBinding binding)
        {
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (binding.Descriptor.Manifest.RequiredServices is { Count: > 0 } manifestRequired)
            {
                foreach (var service in manifestRequired)
                {
                    required.Add(service);
                }
            }

            if (binding.Metadata.RequiredServices is { Count: > 0 } metadataRequired)
            {
                foreach (var service in metadataRequired)
                {
                    required.Add(service);
                }
            }

            if (required.Count == 0)
            {
                return;
            }

            foreach (var service in required)
            {
                if (string.IsNullOrWhiteSpace(service))
                {
                    throw new InvalidOperationException(
                        $"Engine '{binding.Descriptor.ModuleKey}/{binding.Descriptor.Manifest.Id}' declares an empty required service identifier.");
                }
            }

            var validator = services.GetService<IRequiredServiceValidator>();
            if (validator is null)
            {
                throw new InvalidOperationException(
                    $"Engine '{binding.Descriptor.ModuleKey}/{binding.Descriptor.Manifest.Id}' declares required services but no {nameof(IRequiredServiceValidator)} is registered.");
            }

            var missing = validator.GetMissingServices(required.ToArray()) ?? Array.Empty<string>();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Engine '{binding.Descriptor.ModuleKey}/{binding.Descriptor.Manifest.Id}' is missing required services: {string.Join(", ", missing)}.");
            }
        }

        private static async Task InvokeEngineAsync(
            object engine,
            Type payloadType,
            WebhookEventContext context,
            object payload,
            CancellationToken cancellationToken)
        {
            var requestType = typeof(ModuleWebhookRequest<>).MakeGenericType(payloadType);
            var request = Activator.CreateInstance(
                requestType,
                context,
                payload)!;

            var handleMethod = engine.GetType().GetMethod("HandleAsync", new[] { requestType, typeof(CancellationToken) });
            if (handleMethod == null)
            {
                throw new InvalidOperationException($"Engine '{engine.GetType().Name}' does not implement the expected webhook contract.");
            }

            var task = (Task)handleMethod.Invoke(engine, new[] { request, cancellationToken })!;
            await task.ConfigureAwait(false);
        }
    }

    private static bool TryGetHeaderValue(IReadOnlyDictionary<string, string> headers, string? headerName, out string value)
    {
        if (!string.IsNullOrWhiteSpace(headerName)
            && headers.TryGetValue(headerName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue))
        {
            value = headerValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed record WebhookMetadataEntry(IModuleEngineDescriptor Descriptor, ModuleEngineWebhookMetadata Metadata);

    private sealed record ModuleWebhookEngineBinding(
        IModuleEngineDescriptor Descriptor,
        ModuleEngineWebhookMetadata Metadata,
        Type PayloadType);

    private sealed record ModuleWebhookEventDefinition(
        string EventType,
        ModuleEngineSecurity? Security,
        IReadOnlyList<ModuleWebhookEngineBinding> Bindings);

    private static IWebhookAuthenticator BuildAuthenticator(
        string providerName,
        IServiceProvider services,
        ModuleWebhookOptions options,
        IReadOnlyDictionary<string, ModuleWebhookEventDefinition> events)
    {
        if (options.Authenticators.Count == 0)
        {
            return new ModuleWebhookAuthenticator(events, options);
        }

        var context = new ModuleWebhookAuthenticatorContext(providerName, services);
        var authenticators = options.Authenticators
            .Select(factory => factory(context))
            .OfType<IWebhookAuthenticator>()
            .ToArray();

        if (authenticators.Length == 0)
        {
            return new ModuleWebhookAuthenticator(events, options);
        }

        return authenticators.Length == 1
            ? authenticators[0]
            : new CompositeWebhookAuthenticator(authenticators);
    }
}
