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

using Bravellian.Platform;
using Bravellian.Platform.Email;
using Bravellian.Platform.Email.Postmark;
using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Email.AspNetCore;

/// <summary>
/// ASP.NET Core registration helpers for the email outbox.
/// </summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core email outbox services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOutboxOptions">Optional outbox options configuration.</param>
    /// <param name="configureProcessorOptions">Optional processor options configuration.</param>
    /// <param name="configureValidationOptions">Optional validation options configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBravellianEmailCore(
        this IServiceCollection services,
        Action<EmailOutboxOptions>? configureOutboxOptions = null,
        Action<EmailOutboxProcessorOptions>? configureProcessorOptions = null,
        Action<EmailValidationOptions>? configureValidationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOutboxOptions != null)
        {
            services.Configure(configureOutboxOptions);
        }

        if (configureProcessorOptions != null)
        {
            services.Configure(configureProcessorOptions);
        }

        if (configureValidationOptions != null)
        {
            services.Configure(configureValidationOptions);
        }

        services.AddOptions<EmailOutboxOptions>();
        services.AddOptions<EmailOutboxProcessorOptions>();
        services.AddOptions<EmailValidationOptions>();

        services.AddSingleton(sp => new EmailMessageValidator(sp.GetService<IOptions<EmailValidationOptions>>()?.Value));
        services.AddSingleton<IEmailOutbox>(sp => new EmailOutbox(
            sp.GetRequiredService<IOutbox>(),
            sp.GetRequiredService<IEmailDeliverySink>(),
            sp.GetService<IPlatformEventEmitter>(),
            sp.GetService<EmailMessageValidator>(),
            sp.GetService<IOptions<EmailOutboxOptions>>()?.Value));
        services.AddSingleton<IEmailOutboxProcessor>(sp => new EmailOutboxProcessor(
            sp.GetRequiredService<IOutboxStore>(),
            sp.GetRequiredService<IOutboundEmailSender>(),
            sp.GetRequiredService<IIdempotencyStore>(),
            sp.GetRequiredService<IEmailDeliverySink>(),
            sp.GetService<IOutboundEmailProbe>(),
            sp.GetService<IPlatformEventEmitter>(),
            sp.GetService<IEmailSendPolicy>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<IOptions<EmailOutboxProcessorOptions>>()?.Value));

        return services;
    }

    /// <summary>
    /// Registers the Postmark sender adapter.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Optional Postmark options configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBravellianEmailPostmark(
        this IServiceCollection services,
        Action<PostmarkOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddOptions<PostmarkOptions>();
        services.AddOptions<PostmarkValidationOptions>();
        services.AddSingleton<IPostmarkEmailValidator>(sp =>
            new PostmarkEmailValidator(sp.GetRequiredService<IOptions<PostmarkValidationOptions>>().Value));
        services.AddHttpClient<PostmarkOutboundMessageClient>()
            .AddTypedClient((httpClient, sp) =>
                new PostmarkOutboundMessageClient(httpClient, sp.GetRequiredService<IOptions<PostmarkOptions>>().Value));
        services.AddSingleton<IOutboundEmailProbe>(sp =>
            new PostmarkEmailProbe(sp.GetRequiredService<PostmarkOutboundMessageClient>()));
        services.AddHttpClient<PostmarkEmailSender>()
            .AddTypedClient((httpClient, sp) =>
                new PostmarkEmailSender(
                    httpClient,
                    sp.GetRequiredService<IOptions<PostmarkOptions>>().Value,
                    sp.GetRequiredService<IPostmarkEmailValidator>()));
        services.AddTransient<IOutboundEmailSender>(sp => sp.GetRequiredService<PostmarkEmailSender>());
        return services;
    }

    /// <summary>
    /// Registers a hosted service that periodically runs the email outbox processor.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Optional processing options configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBravellianEmailProcessingHostedService(
        this IServiceCollection services,
        Action<EmailProcessingOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddOptions<EmailProcessingOptions>();
        services.AddHostedService<EmailProcessingHostedService>();
        return services;
    }

    /// <summary>
    /// Registers a hosted service that periodically cleans up idempotency records.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Optional cleanup options configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBravellianEmailIdempotencyCleanupHostedService(
        this IServiceCollection services,
        Action<EmailIdempotencyCleanupOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new EmailIdempotencyCleanupOptions();
        configureOptions?.Invoke(options);

        var validator = new EmailIdempotencyCleanupOptionsValidator();
        var validation = validator.Validate(Options.DefaultName, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(EmailIdempotencyCleanupOptions),
                validation.Failures);
        }

        services.AddOptions<EmailIdempotencyCleanupOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailIdempotencyCleanupOptions>>(validator));

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddHostedService<EmailIdempotencyCleanupService>();
        return services;
    }
}

