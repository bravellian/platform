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

using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Request envelope provided to module webhook engines.
/// </summary>
/// <param name="Context">Webhook processing context.</param>
/// <param name="Payload">Parsed payload.</param>
public sealed record ModuleWebhookRequest<TPayload>(
    WebhookEventContext Context,
    TPayload Payload);
