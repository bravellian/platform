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
/// Represents the result of sending an outbound email.
/// </summary>
public sealed record EmailSendResult
{
    private EmailSendResult(
        EmailDeliveryStatus status,
        string? providerMessageId,
        EmailFailureType failureType,
        string? errorCode,
        string? errorMessage)
    {
        Status = status;
        ProviderMessageId = providerMessageId;
        FailureType = failureType;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the delivery status.
    /// </summary>
    public EmailDeliveryStatus Status { get; }

    /// <summary>
    /// Gets the provider message identifier.
    /// </summary>
    public string? ProviderMessageId { get; }

    /// <summary>
    /// Gets the failure type.
    /// </summary>
    public EmailFailureType FailureType { get; }

    /// <summary>
    /// Gets the provider error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the provider error message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful send result.
    /// </summary>
    /// <param name="providerMessageId">Optional provider message id.</param>
    /// <returns>Success result.</returns>
    public static EmailSendResult Success(string? providerMessageId = null)
    {
        return new EmailSendResult(EmailDeliveryStatus.Sent, providerMessageId, EmailFailureType.None, null, null);
    }

    /// <summary>
    /// Creates a transient failure result.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <param name="errorMessage">Error message.</param>
    /// <returns>Transient failure.</returns>
    public static EmailSendResult TransientFailure(string? errorCode = null, string? errorMessage = null)
    {
        return new EmailSendResult(EmailDeliveryStatus.FailedTransient, null, EmailFailureType.Transient, errorCode, errorMessage);
    }

    /// <summary>
    /// Creates a permanent failure result.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <param name="errorMessage">Error message.</param>
    /// <returns>Permanent failure.</returns>
    public static EmailSendResult PermanentFailure(string? errorCode = null, string? errorMessage = null)
    {
        return new EmailSendResult(EmailDeliveryStatus.FailedPermanent, null, EmailFailureType.Permanent, errorCode, errorMessage);
    }
}
