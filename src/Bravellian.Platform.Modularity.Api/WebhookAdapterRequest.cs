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

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Raw webhook request envelope understood by the transport adapters.
/// </summary>
/// <param name="Provider">Webhook provider identifier.</param>
/// <param name="EventType">Webhook event type.</param>
/// <param name="Headers">Raw headers supplied by the gateway.</param>
/// <param name="RawBody">Raw body text for signature validation.</param>
/// <param name="IdempotencyKey">Idempotency key supplied by provider.</param>
/// <param name="Attempt">Delivery attempt number.</param>
/// <param name="Signature">Optional supplied signature.</param>
/// <param name="Payload">Parsed payload DTO.</param>
public sealed record WebhookAdapterRequest<TPayload>(
    string Provider,
    string EventType,
    IReadOnlyDictionary<string, string> Headers,
    string RawBody,
    string IdempotencyKey,
    int Attempt,
    string? Signature,
    TPayload Payload);
