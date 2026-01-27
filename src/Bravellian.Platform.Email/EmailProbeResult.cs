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
/// Represents the outcome of an outbound email probe.
/// </summary>
public sealed record EmailProbeResult
{
    private EmailProbeResult(
        EmailProbeOutcome outcome,
        EmailDeliveryStatus? status,
        string? providerMessageId,
        string? errorCode,
        string? errorMessage)
    {
        Outcome = outcome;
        Status = status;
        ProviderMessageId = providerMessageId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the probe outcome.
    /// </summary>
    public EmailProbeOutcome Outcome { get; }

    /// <summary>
    /// Gets the confirmed delivery status, if available.
    /// </summary>
    public EmailDeliveryStatus? Status { get; }

    /// <summary>
    /// Gets the provider message identifier, if available.
    /// </summary>
    public string? ProviderMessageId { get; }

    /// <summary>
    /// Gets the provider error code, if available.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the provider error message, if available.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a confirmation result.
    /// </summary>
    public static EmailProbeResult Confirmed(
        EmailDeliveryStatus status,
        string? providerMessageId = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return new EmailProbeResult(EmailProbeOutcome.Confirmed, status, providerMessageId, errorCode, errorMessage);
    }

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    public static EmailProbeResult NotFound()
    {
        return new EmailProbeResult(EmailProbeOutcome.NotFound, null, null, null, null);
    }

    /// <summary>
    /// Creates an unknown result.
    /// </summary>
    public static EmailProbeResult Unknown(string? errorCode = null, string? errorMessage = null)
    {
        return new EmailProbeResult(EmailProbeOutcome.Unknown, null, null, errorCode, errorMessage);
    }
}
