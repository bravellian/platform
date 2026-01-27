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

using System.Globalization;

namespace Bravellian.Platform.Email;

/// <summary>
/// Validates outbound email messages.
/// </summary>
public sealed class EmailMessageValidator
{
    private static readonly IReadOnlyList<string> MessageRequiredErrors = new[] { "Message is required." };
    private readonly EmailValidationOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailMessageValidator"/> class.
    /// </summary>
    /// <param name="options">Validation options.</param>
    public EmailMessageValidator(EmailValidationOptions? options = null)
    {
        this.options = options ?? new EmailValidationOptions();
    }

    /// <summary>
    /// Validates the provided message.
    /// </summary>
    /// <param name="message">Outbound email message.</param>
    /// <returns>Validation result.</returns>
    public ValidationResult Validate(OutboundEmailMessage message)
    {
        if (message is null)
        {
            return ValidationResult.Failure(MessageRequiredErrors);
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(message.MessageKey))
        {
            errors.Add("MessageKey is required.");
        }

        if (message.From == null || !IsValidAddress(message.From.Address))
        {
            errors.Add("From address is required and must be valid.");
        }

        if (message.To == null || message.To.Count == 0)
        {
            errors.Add("At least one To recipient is required.");
        }
        else if (message.To.Any(address => !IsValidAddress(address.Address)))
        {
            errors.Add("All To recipients must be valid.");
        }

        if (message.Cc.Any(address => !IsValidAddress(address.Address)))
        {
            errors.Add("All Cc recipients must be valid.");
        }

        if (message.Bcc.Any(address => !IsValidAddress(address.Address)))
        {
            errors.Add("All Bcc recipients must be valid.");
        }

        if (message.ReplyTo != null && !IsValidAddress(message.ReplyTo.Address))
        {
            errors.Add("ReplyTo must be a valid address when provided.");
        }

        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            errors.Add("Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(message.TextBody) && string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            errors.Add("Either TextBody or HtmlBody must be provided.");
        }

        ValidateAttachments(message, errors);

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    private void ValidateAttachments(OutboundEmailMessage message, List<string> errors)
    {
        if (message.Attachments.Count == 0)
        {
            return;
        }

        long totalBytes = 0;
        foreach (var attachment in message.Attachments)
        {
            if (attachment == null)
            {
                errors.Add("Attachments must not contain null entries.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                errors.Add("Attachment file name is required.");
            }

            if (string.IsNullOrWhiteSpace(attachment.ContentType))
            {
                errors.Add("Attachment content type is required.");
            }

            if (attachment.ContentBytes == null || attachment.ContentBytes.Length == 0)
            {
                errors.Add("Attachment content bytes are required.");
            }
            else
            {
                totalBytes += attachment.ContentBytes.Length;
                if (options.MaxAttachmentBytes.HasValue && attachment.ContentBytes.Length > options.MaxAttachmentBytes.Value)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Attachment '{0}' exceeds the maximum size of {1} bytes.",
                        attachment.FileName,
                        options.MaxAttachmentBytes.Value));
                }
            }
        }

        if (options.MaxTotalAttachmentBytes.HasValue && totalBytes > options.MaxTotalAttachmentBytes.Value)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Total attachment size exceeds the maximum of {0} bytes.",
                options.MaxTotalAttachmentBytes.Value));
        }
    }

    private static bool IsValidAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (address.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var atIndex = address.AsSpan().IndexOf("@", StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex == address.Length - 1)
        {
            return false;
        }

        var dotIndex = address.AsSpan().LastIndexOf(".", StringComparison.Ordinal);
        if (dotIndex < atIndex + 2 || dotIndex == address.Length - 1)
        {
            return false;
        }

        return true;
    }
}

