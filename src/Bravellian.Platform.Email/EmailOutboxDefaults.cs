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

using System.Diagnostics.CodeAnalysis;

namespace Bravellian.Platform.Email;

/// <summary>
/// Default settings and helpers for the email outbox.
/// </summary>
public static class EmailOutboxDefaults
{
    /// <summary>
    /// Default outbox topic used for outbound email messages.
    /// </summary>
    public const string Topic = "email.send";

    /// <summary>
    /// Default exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <returns>Delay before next attempt.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Jitter is used for retry dispersion, not security.")]
    public static TimeSpan DefaultBackoff(int attempt)
    {
        var baseMs = Math.Min(60_000, (int)(Math.Pow(2, Math.Min(10, attempt)) * 250));
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }
}
