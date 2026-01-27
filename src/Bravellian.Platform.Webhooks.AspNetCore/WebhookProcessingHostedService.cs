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

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Webhooks.AspNetCore;

/// <summary>
/// Hosted service that periodically runs the webhook processor.
/// </summary>
public sealed class WebhookProcessingHostedService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    private readonly IWebhookProcessor processor;
    private readonly IOptions<WebhookProcessingOptions> options;
    private readonly ILogger<WebhookProcessingHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookProcessingHostedService"/> class.
    /// </summary>
    /// <param name="processor">Webhook processor.</param>
    /// <param name="options">Processing options.</param>
    /// <param name="logger">Logger instance.</param>
    public WebhookProcessingHostedService(
        IWebhookProcessor processor,
        IOptions<WebhookProcessingOptions> options,
        ILogger<WebhookProcessingHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this.processor = processor;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Background service should continue processing after transient failures.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook processing run failed.");
            }

            var delay = options.Value.PollInterval;
            if (delay < MinimumInterval)
            {
                delay = MinimumInterval;
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
