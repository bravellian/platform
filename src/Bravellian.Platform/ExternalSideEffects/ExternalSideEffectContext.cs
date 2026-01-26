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

namespace Bravellian.Platform;

/// <summary>
/// Encapsulates context for executing an external side effect.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="Message">The originating outbox message.</param>
/// <param name="Payload">The deserialized payload.</param>
/// <param name="Request">The external side-effect request.</param>
public sealed record ExternalSideEffectContext<TPayload>(
    OutboxMessage Message,
    TPayload Payload,
    ExternalSideEffectRequest Request);
