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

using System.Net;

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Result from webhook ingestion that drives the HTTP response and downstream handling.
/// </summary>
/// <param name="Decision">Ingestion decision.</param>
/// <param name="HttpStatusCode">HTTP status code to return to the provider.</param>
/// <param name="Reason">Optional reason for the decision.</param>
/// <param name="ProviderEventId">Optional provider event identifier.</param>
/// <param name="EventType">Optional event type identifier.</param>
/// <param name="DedupeKey">Optional dedupe key.</param>
/// <param name="PartitionKey">Optional partition key for multi-tenant routing.</param>
/// <param name="ParsedSummaryJson">Optional JSON summary of the parsed payload.</param>
/// <param name="Duplicate">Whether the webhook was identified as a duplicate.</param>
public sealed record WebhookIngestResult(
    WebhookIngestDecision Decision,
    HttpStatusCode HttpStatusCode,
    string? Reason,
    string? ProviderEventId,
    string? EventType,
    string? DedupeKey,
    string? PartitionKey,
    string? ParsedSummaryJson,
    bool Duplicate);
