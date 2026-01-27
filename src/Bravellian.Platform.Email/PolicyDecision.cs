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
/// Represents a policy decision for sending an email.
/// </summary>
public sealed record PolicyDecision
{
    private PolicyDecision(EmailPolicyOutcome outcome, string? reason, DateTimeOffset? delayUntilUtc)
    {
        Outcome = outcome;
        Reason = reason;
        DelayUntilUtc = delayUntilUtc;
    }

    /// <summary>
    /// Gets the policy outcome.
    /// </summary>
    public EmailPolicyOutcome Outcome { get; }

    /// <summary>
    /// Gets the optional reason for the decision.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the time to delay until, when applicable.
    /// </summary>
    public DateTimeOffset? DelayUntilUtc { get; }

    /// <summary>
    /// Creates an allow decision.
    /// </summary>
    /// <returns>Allow decision.</returns>
    public static PolicyDecision Allow()
    {
        return new PolicyDecision(EmailPolicyOutcome.Allow, null, null);
    }

    /// <summary>
    /// Creates a delay decision.
    /// </summary>
    /// <param name="delayUntilUtc">Delay until timestamp.</param>
    /// <param name="reason">Optional reason.</param>
    /// <returns>Delay decision.</returns>
    public static PolicyDecision Delay(DateTimeOffset delayUntilUtc, string? reason = null)
    {
        return new PolicyDecision(EmailPolicyOutcome.Delay, reason, delayUntilUtc);
    }

    /// <summary>
    /// Creates a rejection decision.
    /// </summary>
    /// <param name="reason">Optional reason.</param>
    /// <returns>Reject decision.</returns>
    public static PolicyDecision Reject(string? reason = null)
    {
        return new PolicyDecision(EmailPolicyOutcome.Reject, reason, null);
    }
}
