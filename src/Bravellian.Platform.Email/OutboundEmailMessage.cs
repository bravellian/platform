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
/// Represents an outbound email message.
/// </summary>
public sealed record OutboundEmailMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundEmailMessage"/> class.
    /// </summary>
    /// <param name="messageKey">Stable idempotency key.</param>
    /// <param name="from">Sender address.</param>
    /// <param name="to">Primary recipients.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="textBody">Plain text body.</param>
    /// <param name="htmlBody">HTML body.</param>
    /// <param name="cc">Carbon copy recipients.</param>
    /// <param name="bcc">Blind carbon copy recipients.</param>
    /// <param name="replyTo">Reply-to address.</param>
    /// <param name="attachments">Optional attachments.</param>
    /// <param name="headers">Optional provider headers.</param>
    /// <param name="metadata">Optional provider-agnostic metadata.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="requestedSendAtUtc">Optional requested send time.</param>
    public OutboundEmailMessage(
        string messageKey,
        EmailAddress from,
        IReadOnlyList<EmailAddress> to,
        string subject,
        string? textBody = null,
        string? htmlBody = null,
        IReadOnlyList<EmailAddress>? cc = null,
        IReadOnlyList<EmailAddress>? bcc = null,
        EmailAddress? replyTo = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? requestedSendAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
        {
            throw new ArgumentException("Message key is required.", nameof(messageKey));
        }

        MessageKey = messageKey.Trim();
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = NormalizeRecipients(to, true, nameof(to));
        Cc = NormalizeRecipients(cc, false, nameof(cc));
        Bcc = NormalizeRecipients(bcc, false, nameof(bcc));
        ReplyTo = replyTo;
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        TextBody = textBody;
        HtmlBody = htmlBody;
        Attachments = attachments?.ToArray() ?? Array.Empty<EmailAttachment>();
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Tags = tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToArray()
            ?? Array.Empty<string>();
        RequestedSendAtUtc = requestedSendAtUtc;
    }

    /// <summary>
    /// Gets the stable idempotency key.
    /// </summary>
    public string MessageKey { get; }

    /// <summary>
    /// Gets the sender address.
    /// </summary>
    public EmailAddress From { get; }

    /// <summary>
    /// Gets the primary recipients.
    /// </summary>
    public IReadOnlyList<EmailAddress> To { get; }

    /// <summary>
    /// Gets the carbon copy recipients.
    /// </summary>
    public IReadOnlyList<EmailAddress> Cc { get; }

    /// <summary>
    /// Gets the blind carbon copy recipients.
    /// </summary>
    public IReadOnlyList<EmailAddress> Bcc { get; }

    /// <summary>
    /// Gets the reply-to address.
    /// </summary>
    public EmailAddress? ReplyTo { get; }

    /// <summary>
    /// Gets the subject line.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// Gets the plain text body.
    /// </summary>
    public string? TextBody { get; }

    /// <summary>
    /// Gets the HTML body.
    /// </summary>
    public string? HtmlBody { get; }

    /// <summary>
    /// Gets optional attachments.
    /// </summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; }

    /// <summary>
    /// Gets optional headers for provider-specific data.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets optional provider-agnostic metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Gets optional tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Gets the optional requested send time.
    /// </summary>
    public DateTimeOffset? RequestedSendAtUtc { get; }

    private static EmailAddress[] NormalizeRecipients(
        IEnumerable<EmailAddress>? recipients,
        bool required,
        string paramName)
    {
        if (recipients == null)
        {
            if (required)
            {
                throw new ArgumentNullException(paramName);
            }

            return Array.Empty<EmailAddress>();
        }

        var list = recipients.Where(address => address != null).ToArray();
        if (required && list.Length == 0)
        {
            throw new ArgumentException("At least one recipient is required.", paramName);
        }

        return list;
    }
}

