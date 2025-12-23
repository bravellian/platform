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
/// Incoming webhook request passed to webhook engines.
/// </summary>
/// <param name="Provider">Provider identifier.</param>
/// <param name="EventType">Event type identifier.</param>
/// <param name="Payload">Deserialized payload.</param>
/// <param name="IdempotencyKey">Idempotency key for replay protection.</param>
/// <param name="Attempt">Current delivery attempt; 0 means unknown, values >= 1 are attempt counts (first attempt is 1).</param>
public sealed record WebhookRequest<TPayload>(
    string Provider,
    string EventType,
    TPayload Payload,
    string IdempotencyKey,
    int Attempt = 0);
