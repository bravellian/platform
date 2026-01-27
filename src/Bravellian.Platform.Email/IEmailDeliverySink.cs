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
/// Defines a sink for recording outbound email delivery events.
/// </summary>
public interface IEmailDeliverySink
{
    /// <summary>
    /// Records that an email has been queued.
    /// </summary>
    /// <param name="message">Outbound email message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordQueuedAsync(OutboundEmailMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Records a delivery attempt.
    /// </summary>
    /// <param name="message">Outbound email message.</param>
    /// <param name="attempt">Delivery attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAttemptAsync(OutboundEmailMessage message, EmailDeliveryAttempt attempt, CancellationToken cancellationToken);

    /// <summary>
    /// Records the final delivery status.
    /// </summary>
    /// <param name="message">Outbound email message.</param>
    /// <param name="status">Final delivery status.</param>
    /// <param name="providerMessageId">Provider message id.</param>
    /// <param name="errorCode">Provider error code.</param>
    /// <param name="errorMessage">Provider error message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordFinalAsync(
        OutboundEmailMessage message,
        EmailDeliveryStatus status,
        string? providerMessageId,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an external delivery update from a provider (for example, webhook notifications).
    /// </summary>
    /// <param name="update">Delivery update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordExternalAsync(EmailDeliveryUpdate update, CancellationToken cancellationToken);
}

