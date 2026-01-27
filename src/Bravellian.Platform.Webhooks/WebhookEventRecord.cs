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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Normalized webhook record written to an inbox store.
/// </summary>
/// <param name="Provider">Webhook provider identifier.</param>
/// <param name="ReceivedAtUtc">UTC timestamp when the webhook was received.</param>
/// <param name="ProviderEventId">Optional provider event identifier.</param>
/// <param name="EventType">Optional event type identifier.</param>
/// <param name="DedupeKey">Dedupe key used for idempotency.</param>
/// <param name="PartitionKey">Optional partition key for multi-tenant routing.</param>
/// <param name="HeadersJson">Serialized headers captured from the request.</param>
/// <param name="BodyBytes">Raw request body bytes.</param>
/// <param name="ContentType">Optional request content type.</param>
/// <param name="Status">Current processing status.</param>
/// <param name="AttemptCount">Current attempt count.</param>
/// <param name="NextAttemptUtc">Next attempt time when retrying.</param>
#pragma warning disable CA1819
public sealed record WebhookEventRecord(
    string Provider,
    DateTimeOffset ReceivedAtUtc,
    string? ProviderEventId,
    string? EventType,
    string DedupeKey,
    string? PartitionKey,
    string HeadersJson,
    [property: SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "Raw body bytes are required for signature validation and processing.")] byte[] BodyBytes,
    string? ContentType,
    WebhookEventStatus Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptUtc);
#pragma warning restore CA1819
