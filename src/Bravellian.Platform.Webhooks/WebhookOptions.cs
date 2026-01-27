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
/// Configuration options for webhook ingestion behavior.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether rejected webhook requests should be stored.
    /// </summary>
    public bool StoreRejected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether rejected webhook payloads should be redacted.
    /// </summary>
    public bool RedactRejectedBody { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked after ingestion completes.
    /// </summary>
    public Action<WebhookIngestResult, WebhookEnvelope>? OnIngested { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked after processing completes.
    /// </summary>
    public Action<ProcessingResult, WebhookEventContext>? OnProcessed { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked when a webhook is rejected.
    /// </summary>
    public Action<string?, WebhookEnvelope, WebhookIngestResult?>? OnRejected { get; set; }
}
