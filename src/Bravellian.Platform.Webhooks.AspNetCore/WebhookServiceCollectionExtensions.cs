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
using Microsoft.Extensions.Options;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Webhooks.AspNetCore;

/// <summary>
/// Service registration extensions for webhook ingestion and processing.
/// </summary>
public static class WebhookServiceCollectionExtensions
{
    /// <summary>
    /// Registers Bravellian webhook services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Optional webhook option configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBravellianWebhooks(
        this IServiceCollection services,
        Action<WebhookOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddOptions<WebhookProcessingOptions>();
        services.AddSingleton<IWebhookProviderRegistry, WebhookProviderRegistry>();
        services.AddSingleton<IPostConfigureOptions<WebhookOptions>, WebhookLoggingOptionsSetup>();
        services.AddSingleton<IWebhookIngestor>(sp =>
        {
            var registry = sp.GetRequiredService<IWebhookProviderRegistry>();
            var inbox = sp.GetRequiredService<Bravellian.Platform.IInbox>();
            var timeProvider = sp.GetService<TimeProvider>();
            var options = sp.GetService<IOptions<WebhookOptions>>()?.Value;
            var inboxRouter = sp.GetService<Bravellian.Platform.IInboxRouter>();
            var partitionResolver = sp.GetService<IWebhookPartitionResolver>();
            return new WebhookIngestor(registry, inbox, timeProvider, options, inboxRouter, partitionResolver);
        });
        services.AddSingleton<IWebhookProcessor>(sp =>
        {
            var workStore = sp.GetRequiredService<Bravellian.Platform.IInboxWorkStore>();
            var registry = sp.GetRequiredService<IWebhookProviderRegistry>();
            var options = sp.GetService<IOptions<WebhookOptions>>()?.Value;
            var processing = sp.GetService<IOptions<WebhookProcessingOptions>>()?.Value;
            var processorOptions = processing == null
                ? null
                : new WebhookProcessorOptions
                {
                    BatchSize = processing.BatchSize,
                    MaxAttempts = processing.MaxAttempts,
                };
            return new WebhookProcessor(workStore, registry, processorOptions, options);
        });

        return services;
    }

    /// <summary>
    /// Registers a hosted service that periodically runs the webhook processor.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddWebhookProcessingHostedService(this IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddHostedService<WebhookProcessingHostedService>();
        return services;
    }
}
