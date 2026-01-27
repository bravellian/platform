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
/// Defines how the processor should behave when no handler is registered.
/// </summary>
public enum WebhookMissingHandlerBehavior
{
    /// <summary>
    /// Treat missing handlers as completed and acknowledge the message.
    /// </summary>
    Complete,

    /// <summary>
    /// Treat missing handlers as retryable failures.
    /// </summary>
    Retry,

    /// <summary>
    /// Mark messages as poisoned immediately when no handler is found.
    /// </summary>
    Poison,
}
