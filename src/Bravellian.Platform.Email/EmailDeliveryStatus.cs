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

namespace Bravellian.Platform.Email;

/// <summary>
/// Represents provider-neutral delivery states.
/// </summary>
public enum EmailDeliveryStatus
{
    /// <summary>
    /// Message is queued for delivery.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Message was sent successfully.
    /// </summary>
    Sent = 1,

    /// <summary>
    /// Message failed with a transient error.
    /// </summary>
    FailedTransient = 2,

    /// <summary>
    /// Message failed with a permanent error.
    /// </summary>
    FailedPermanent = 3,

    /// <summary>
    /// Message was bounced by the provider or recipient.
    /// </summary>
    Bounced = 4,

    /// <summary>
    /// Message was suppressed by policy or provider rules.
    /// </summary>
    Suppressed = 5
}
