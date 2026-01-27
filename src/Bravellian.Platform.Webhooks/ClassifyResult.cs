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
/// Classification outcome for a webhook request.
/// </summary>
/// <param name="Decision">Ingestion decision.</param>
/// <param name="ProviderEventId">Optional provider event identifier.</param>
/// <param name="EventType">Optional event type identifier.</param>
/// <param name="DedupeKey">Optional dedupe key.</param>
/// <param name="PartitionKey">Optional partition key for multi-tenant routing.</param>
/// <param name="ParsedSummaryJson">Optional JSON summary of the parsed payload.</param>
/// <param name="FailureReason">Optional failure reason.</param>
public sealed record ClassifyResult(
    WebhookIngestDecision Decision,
    string? ProviderEventId,
    string? EventType,
    string? DedupeKey,
    string? PartitionKey,
    string? ParsedSummaryJson,
    string? FailureReason);
