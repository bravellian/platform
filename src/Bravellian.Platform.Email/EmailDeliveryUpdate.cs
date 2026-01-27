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
/// Represents an external email delivery update from a provider.
/// </summary>
/// <param name="MessageKey">Optional message key for correlation.</param>
/// <param name="ProviderMessageId">Optional provider message identifier.</param>
/// <param name="ProviderEventId">Optional provider event identifier.</param>
/// <param name="Status">Delivery status.</param>
/// <param name="ErrorCode">Optional provider error code.</param>
/// <param name="ErrorMessage">Optional provider error message.</param>
public sealed record EmailDeliveryUpdate(
    string? MessageKey,
    string? ProviderMessageId,
    string? ProviderEventId,
    EmailDeliveryStatus Status,
    string? ErrorCode,
    string? ErrorMessage);
