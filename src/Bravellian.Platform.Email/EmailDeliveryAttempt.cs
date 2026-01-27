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
/// Represents a delivery attempt for an outbound email.
/// </summary>
public sealed record EmailDeliveryAttempt
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDeliveryAttempt"/> class.
    /// </summary>
    /// <param name="attemptNumber">Attempt sequence number.</param>
    /// <param name="timestampUtc">Attempt timestamp.</param>
    /// <param name="status">Delivery status.</param>
    /// <param name="providerMessageId">Provider message id.</param>
    /// <param name="errorCode">Provider error code.</param>
    /// <param name="errorMessage">Provider error message.</param>
    public EmailDeliveryAttempt(
        int attemptNumber,
        DateTimeOffset timestampUtc,
        EmailDeliveryStatus status,
        string? providerMessageId = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        AttemptNumber = attemptNumber;
        TimestampUtc = timestampUtc;
        Status = status;
        ProviderMessageId = providerMessageId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the attempt sequence number.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the attempt timestamp.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Gets the delivery status.
    /// </summary>
    public EmailDeliveryStatus Status { get; }

    /// <summary>
    /// Gets the provider message id.
    /// </summary>
    public string? ProviderMessageId { get; }

    /// <summary>
    /// Gets the provider error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the provider error message.
    /// </summary>
    public string? ErrorMessage { get; }
}
