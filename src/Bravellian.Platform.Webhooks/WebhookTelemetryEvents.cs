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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Common telemetry event names for webhook processing.
/// </summary>
public static class WebhookTelemetryEvents
{
    /// <summary>
    /// Webhook ingest accepted event name.
    /// </summary>
    public const string IngestAccepted = "webhooks.ingest.accepted";

    /// <summary>
    /// Webhook ingest ignored event name.
    /// </summary>
    public const string IngestIgnored = "webhooks.ingest.ignored";

    /// <summary>
    /// Webhook ingest rejected event name.
    /// </summary>
    public const string IngestRejected = "webhooks.ingest.rejected";

    /// <summary>
    /// Webhook ingest duplicate event name.
    /// </summary>
    public const string IngestDuplicate = "webhooks.ingest.duplicate";

    /// <summary>
    /// Webhook processing completed event name.
    /// </summary>
    public const string ProcessCompleted = "webhooks.process.completed";

    /// <summary>
    /// Webhook processing retry scheduled event name.
    /// </summary>
    public const string ProcessRetryScheduled = "webhooks.process.retry";

    /// <summary>
    /// Webhook processing poisoned event name.
    /// </summary>
    public const string ProcessPoisoned = "webhooks.process.poisoned";

    /// <summary>
    /// Webhook processing rejected event name.
    /// </summary>
    public const string ProcessRejected = "webhooks.process.rejected";
}
