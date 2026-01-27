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
/// Context provided to webhook handlers for processing.
/// </summary>
/// <param name="Provider">Webhook provider identifier.</param>
/// <param name="DedupeKey">Dedupe key used for idempotency.</param>
/// <param name="ProviderEventId">Optional provider event identifier.</param>
/// <param name="EventType">Optional event type identifier.</param>
/// <param name="PartitionKey">Optional partition key for multi-tenant routing.</param>
/// <param name="ReceivedAtUtc">UTC timestamp when the webhook was received.</param>
/// <param name="Headers">Request headers.</param>
/// <param name="BodyBytes">Raw request body bytes.</param>
/// <param name="ContentType">Optional request content type.</param>
public sealed record WebhookEventContext(
    string Provider,
    string DedupeKey,
    string? ProviderEventId,
    string? EventType,
    string? PartitionKey,
    DateTimeOffset ReceivedAtUtc,
    IReadOnlyDictionary<string, string> Headers,
    byte[] BodyBytes,
    string? ContentType);
