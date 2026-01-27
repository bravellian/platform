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
/// Standard event names for platform observability.
/// </summary>
public static class PlatformEventNames
{
    /// <summary>
    /// Audit event emitted when an outbox message is processed.
    /// </summary>
    public const string OutboxMessageProcessed = "outbox.message.processed";

    /// <summary>
    /// Audit event emitted when a webhook is received.
    /// </summary>
    public const string WebhookReceived = "webhook.received";

    /// <summary>
    /// Audit event emitted when an email is sent.
    /// </summary>
    public const string EmailSent = "email.sent";

    /// <summary>
    /// Audit event emitted when an email is queued.
    /// </summary>
    public const string EmailQueued = "email.queued";

    /// <summary>
    /// Audit event emitted when an email send attempt occurs.
    /// </summary>
    public const string EmailAttempted = "email.attempted";

    /// <summary>
    /// Audit event emitted when an email fails.
    /// </summary>
    public const string EmailFailed = "email.failed";

    /// <summary>
    /// Audit event emitted when an email is suppressed.
    /// </summary>
    public const string EmailSuppressed = "email.suppressed";

    /// <summary>
    /// Audit event emitted when an email bounces.
    /// </summary>
    public const string EmailBounced = "email.bounced";

    /// <summary>
    /// Audit event emitted when an operation starts.
    /// </summary>
    public const string OperationStarted = "operation.started";

    /// <summary>
    /// Audit event emitted when an operation completes.
    /// </summary>
    public const string OperationCompleted = "operation.completed";

    /// <summary>
    /// Audit event emitted when an operation fails.
    /// </summary>
    public const string OperationFailed = "operation.failed";
}
