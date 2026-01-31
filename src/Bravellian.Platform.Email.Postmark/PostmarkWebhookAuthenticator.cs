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

using System.Security.Cryptography;
using System.Text;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Email.Postmark;

internal sealed class PostmarkWebhookAuthenticator : IWebhookAuthenticator
{
    private readonly PostmarkWebhookOptions options;

    public PostmarkWebhookAuthenticator(PostmarkWebhookOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.IsNullOrWhiteSpace(options.SigningSecret))
        {
            if (!TryGetHeader(envelope.Headers, options.SignatureHeader, out var signature))
            {
                return Task.FromResult(new AuthResult(false, "Missing Postmark signature header."));
            }

            if (!ValidateSignature(signature, envelope.BodyBytes, options.SigningSecret))
            {
                return Task.FromResult(new AuthResult(false, "Invalid Postmark signature."));
            }

            return Task.FromResult(new AuthResult(true, null));
        }

        if (!string.IsNullOrWhiteSpace(options.SharedSecret))
        {
            if (!TryGetHeader(envelope.Headers, options.SharedSecretHeader, out var secret))
            {
                return Task.FromResult(new AuthResult(false, "Missing webhook shared secret header."));
            }

            if (!string.Equals(secret, options.SharedSecret, StringComparison.Ordinal))
            {
                return Task.FromResult(new AuthResult(false, "Invalid webhook shared secret."));
            }

            return Task.FromResult(new AuthResult(true, null));
        }

        return Task.FromResult(new AuthResult(true, null));
    }

    private static bool ValidateSignature(string? signature, byte[] bodyBytes, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var normalized = signature.Trim();
        return MatchesSignature(normalized, bodyBytes, secret, useSha256: false)
            || MatchesSignature(normalized, bodyBytes, secret, useSha256: true);
    }

    private static bool MatchesSignature(string signature, byte[] bodyBytes, string secret, bool useSha256)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hash;
        if (useSha256)
        {
            using var hmac = new HMACSHA256(keyBytes);
            hash = hmac.ComputeHash(bodyBytes);
        }
        else
        {
#pragma warning disable CA5350 // Postmark signatures may use HMACSHA1 for legacy compatibility.
            using var hmac = new HMACSHA1(keyBytes);
            hash = hmac.ComputeHash(bodyBytes);
#pragma warning restore CA5350
        }

        var hex = Convert.ToHexString(hash);
        if (string.Equals(signature, hex, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var base64 = Convert.ToBase64String(hash);
        return string.Equals(signature, base64, StringComparison.Ordinal);
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        if (headers.TryGetValue(name, out var direct))
        {
            value = direct;
            return true;
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = header.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
