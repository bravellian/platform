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

using Bravellian.Platform.Email;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Postmark implementation of <see cref="IOutboundEmailProbe"/>.
/// </summary>
public sealed class PostmarkEmailProbe : IOutboundEmailProbe
{
    private const string MessageKeyMetadataKey = "MessageKey";

    private readonly PostmarkOutboundMessageClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkEmailProbe"/> class.
    /// </summary>
    /// <param name="client">Outbound message client.</param>
    public PostmarkEmailProbe(PostmarkOutboundMessageClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<EmailProbeResult> ProbeAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.MessageKey))
        {
            return EmailProbeResult.Unknown("message_key_missing", "Message key is required for probing.");
        }

        var lookup = await client.SearchOutboundByMetadataAsync(MessageKeyMetadataKey, message.MessageKey, cancellationToken)
            .ConfigureAwait(false);

        if (lookup.Status == PostmarkQueryStatus.NotFound)
        {
            return EmailProbeResult.NotFound();
        }

        if (lookup.Status == PostmarkQueryStatus.Error || lookup.Response == null)
        {
            return EmailProbeResult.Unknown("postmark_lookup_failed", lookup.Error);
        }

        var messages = lookup.Response.Messages;
        if (messages == null || messages.Count == 0)
        {
            return EmailProbeResult.NotFound();
        }

        var match = messages[0];
        var status = MapStatus(match.Status);
        return EmailProbeResult.Confirmed(status, match.MessageId);
    }

    private static EmailDeliveryStatus MapStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return EmailDeliveryStatus.Sent;
        }

        if (string.Equals(status, "Bounced", StringComparison.OrdinalIgnoreCase))
        {
            return EmailDeliveryStatus.Bounced;
        }

        if (string.Equals(status, "Suppressed", StringComparison.OrdinalIgnoreCase))
        {
            return EmailDeliveryStatus.Suppressed;
        }

        return EmailDeliveryStatus.Sent;
    }
}
