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
using System.Security.Cryptography;

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Helper methods for generating webhook dedupe keys.
/// </summary>
public static class WebhookDedupe
{
    /// <summary>
    /// Creates a dedupe key using the provider event identifier when available,
    /// or a SHA-256 hash of the body when it is not.
    /// </summary>
    /// <param name="provider">Webhook provider identifier.</param>
    /// <param name="providerEventId">Optional provider event identifier.</param>
    /// <param name="bodyBytes">Request body bytes.</param>
    /// <returns>A dedupe result containing the key and whether it is weak.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="provider"/> is null or whitespace.</exception>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Lowercase hex is a stable, human-friendly format for dedupe keys.")]
    public static WebhookDedupeResult Create(string provider, string? providerEventId, byte[]? bodyBytes)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider is required.", nameof(provider));
        }

        if (!string.IsNullOrWhiteSpace(providerEventId))
        {
            return new WebhookDedupeResult($"{provider}:{providerEventId}", false);
        }

        bodyBytes ??= Array.Empty<byte>();
        var hash = SHA256.HashData(bodyBytes);
        var hashText = Convert.ToHexString(hash).ToLowerInvariant();
        return new WebhookDedupeResult($"{provider}:sha256:{hashText}", true);
    }
}
