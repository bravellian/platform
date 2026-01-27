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

namespace Bravellian.Platform.Observability;

/// <summary>
/// Standard tag keys for platform observability.
/// </summary>
public static class PlatformTagKeys
{
    /// <summary>
    /// Tag for the tenant identifier.
    /// </summary>
    public const string Tenant = "tenant";

    /// <summary>
    /// Tag for the logical partition identifier.
    /// </summary>
    public const string Partition = "partition";

    /// <summary>
    /// Tag for provider identifiers (email/webhook/etc).
    /// </summary>
    public const string Provider = "provider";

    /// <summary>
    /// Tag for a stable message key (idempotency key).
    /// </summary>
    public const string MessageKey = "messageKey";

    /// <summary>
    /// Tag for an operation identifier.
    /// </summary>
    public const string OperationId = "operationId";

    /// <summary>
    /// Tag for an outbox message identifier.
    /// </summary>
    public const string OutboxMessageId = "outboxMessageId";

    /// <summary>
    /// Tag for an inbox message identifier.
    /// </summary>
    public const string InboxMessageId = "inboxMessageId";

    /// <summary>
    /// Tag for a webhook event identifier.
    /// </summary>
    public const string WebhookEventId = "webhookEventId";
}