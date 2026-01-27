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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Email.AspNetCore;

/// <summary>
/// Hosted service that periodically runs the email outbox processor.
/// </summary>
public sealed class EmailProcessingHostedService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    private readonly IEmailOutboxProcessor processor;
    private readonly IOptions<EmailProcessingOptions> options;
    private readonly ILogger<EmailProcessingHostedService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailProcessingHostedService"/> class.
    /// </summary>
    /// <param name="processor">Email outbox processor.</param>
    /// <param name="options">Processing options.</param>
    /// <param name="logger">Logger instance.</param>
    public EmailProcessingHostedService(
        IEmailOutboxProcessor processor,
        IOptions<EmailProcessingOptions> options,
        ILogger<EmailProcessingHostedService> logger)
    {
        this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Email outbox processing run failed.");
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
