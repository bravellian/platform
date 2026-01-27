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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Webhooks.AspNetCore;

internal sealed class WebhookLoggingOptionsSetup : IPostConfigureOptions<WebhookOptions>
{
    private readonly ILogger logger;

    public WebhookLoggingOptionsSetup(ILoggerFactory loggerFactory)
    {
        logger = loggerFactory.CreateLogger("Bravellian.Platform.Webhooks");
    }

    public void PostConfigure(string? name, WebhookOptions options)
    {
        var existingIngested = options.OnIngested;
        var existingProcessed = options.OnProcessed;
        var existingRejected = options.OnRejected;

        options.OnIngested = (result, envelope) =>
        {
            existingIngested?.Invoke(result, envelope);
            LogIngested(result, envelope);
        };

        options.OnProcessed = (result, context) =>
        {
            existingProcessed?.Invoke(result, context);
            LogProcessed(result, context);
        };

        options.OnRejected = (reason, envelope, result) =>
        {
            existingRejected?.Invoke(reason, envelope, result);
            LogRejected(reason, envelope, result);
        };
    }

    private void LogIngested(WebhookIngestResult result, WebhookEnvelope envelope)
    {
        switch (result.Decision)
        {
            case WebhookIngestDecision.Accepted:
                logger.LogInformation(
                    "{EventName} provider={Provider} eventType={EventType} dedupeKey={DedupeKey}",
                    WebhookTelemetryEvents.IngestAccepted,
                    envelope.Provider,
                    result.EventType,
                    result.DedupeKey);
                if (result.Duplicate)
                {
                    logger.LogInformation(
                        "{EventName} provider={Provider} dedupeKey={DedupeKey}",
                        WebhookTelemetryEvents.IngestDuplicate,
                        envelope.Provider,
                        result.DedupeKey);
                }

                break;
            case WebhookIngestDecision.Ignored:
                logger.LogInformation(
                    "{EventName} provider={Provider} eventType={EventType}",
                    WebhookTelemetryEvents.IngestIgnored,
                    envelope.Provider,
                    result.EventType);
                break;
        }
    }

    private void LogProcessed(ProcessingResult result, WebhookEventContext context)
    {
        switch (result.Status)
        {
            case WebhookEventStatus.Completed:
                logger.LogInformation(
                    "{EventName} provider={Provider} eventType={EventType} dedupeKey={DedupeKey} attempt={Attempt}",
                    WebhookTelemetryEvents.ProcessCompleted,
                    context.Provider,
                    context.EventType,
                    context.DedupeKey,
                    result.AttemptCount);
                break;
            case WebhookEventStatus.FailedRetryable:
                logger.LogWarning(
                    "{EventName} provider={Provider} eventType={EventType} dedupeKey={DedupeKey} attempt={Attempt} error={Error}",
                    WebhookTelemetryEvents.ProcessRetryScheduled,
                    context.Provider,
                    context.EventType,
                    context.DedupeKey,
                    result.AttemptCount,
                    result.ErrorMessage);
                break;
            case WebhookEventStatus.Poisoned:
                logger.LogError(
                    "{EventName} provider={Provider} eventType={EventType} dedupeKey={DedupeKey} attempt={Attempt} error={Error}",
                    WebhookTelemetryEvents.ProcessPoisoned,
                    context.Provider,
                    context.EventType,
                    context.DedupeKey,
                    result.AttemptCount,
                    result.ErrorMessage);
                break;
            case WebhookEventStatus.Rejected:
                logger.LogInformation(
                    "{EventName} provider={Provider} dedupeKey={DedupeKey}",
                    WebhookTelemetryEvents.ProcessRejected,
                    context.Provider,
                    context.DedupeKey);
                break;
        }
    }

    private void LogRejected(string? reason, WebhookEnvelope envelope, WebhookIngestResult? result)
    {
        logger.LogWarning(
            "{EventName} provider={Provider} reason={Reason} dedupeKey={DedupeKey}",
            WebhookTelemetryEvents.IngestRejected,
            envelope.Provider,
            reason,
            result?.DedupeKey);
    }
}
