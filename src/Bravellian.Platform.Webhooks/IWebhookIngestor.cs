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
/// Ingests webhook requests into the processing pipeline.
/// </summary>
public interface IWebhookIngestor
{
    /// <summary>
    /// Ingests a webhook for the specified provider.
    /// </summary>
    /// <param name="providerName">Provider name.</param>
    /// <param name="envelope">Webhook envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Webhook ingest result.</returns>
    Task<WebhookIngestResult> IngestAsync(string providerName, WebhookEnvelope envelope, CancellationToken cancellationToken);
}
