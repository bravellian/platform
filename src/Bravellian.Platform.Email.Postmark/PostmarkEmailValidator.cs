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
using System.IO;
using Bravellian.Platform.Email;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Validates outbound email messages against Postmark requirements.
/// </summary>
public sealed class PostmarkEmailValidator : IPostmarkEmailValidator
{
    private readonly PostmarkValidationOptions options;
    private readonly HashSet<string> forbiddenExtensions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkEmailValidator"/> class.
    /// </summary>
    /// <param name="options">Validation options.</param>
    public PostmarkEmailValidator(PostmarkValidationOptions? options = null)
    {
        this.options = options ?? new PostmarkValidationOptions();
        forbiddenExtensions = new HashSet<string>(this.options.ForbiddenExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ValidationResult Validate(OutboundEmailMessage message)
    {
        if (message is null)
        {
            return ValidationResult.Failure(new[] { "Message is required." });
        }

        var errors = new List<string>();
        ValidateBodies(message, errors);
        ValidateAttachments(message, errors);
        ValidateTotalSize(message, errors);

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    private void ValidateBodies(OutboundEmailMessage message, List<string> errors)
    {
        if (options.MaxBodyBytes <= 0)
        {
            return;
        }

        var textBytes = GetByteCount(message.TextBody);
        if (textBytes > options.MaxBodyBytes)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Text body exceeds maximum of {0} bytes.",
                options.MaxBodyBytes));
        }

        var htmlBytes = GetByteCount(message.HtmlBody);
        if (htmlBytes > options.MaxBodyBytes)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "HTML body exceeds maximum of {0} bytes.",
                options.MaxBodyBytes));
        }
    }

    private void ValidateAttachments(OutboundEmailMessage message, List<string> errors)
    {
        if (message.Attachments.Count == 0)
        {
            return;
        }

        foreach (var attachment in message.Attachments)
        {
            var extension = Path.GetExtension(attachment.FileName);
            if (options.EnforceForbiddenExtensions && !string.IsNullOrWhiteSpace(extension) && forbiddenExtensions.Contains(extension))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Attachment '{0}' has forbidden extension '{1}'.",
                    attachment.FileName,
                    extension));
            }

            if (options.MaxAttachmentBytes.HasValue && attachment.ContentBytes.Length > options.MaxAttachmentBytes.Value)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Attachment '{0}' exceeds maximum of {1} bytes.",
                    attachment.FileName,
                    options.MaxAttachmentBytes.Value));
            }
        }
    }

    private void ValidateTotalSize(OutboundEmailMessage message, List<string> errors)
    {
        if (options.MaxMessageBytes <= 0)
        {
            return;
        }

        long totalBytes = GetByteCount(message.TextBody) + GetByteCount(message.HtmlBody);
        if (message.Attachments.Count > 0)
        {
            foreach (var attachment in message.Attachments)
            {
                totalBytes += GetBase64EncodedSize(attachment.ContentBytes.Length);
            }
        }

        if (totalBytes > options.MaxMessageBytes)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Total message size exceeds maximum of {0} bytes.",
                options.MaxMessageBytes));
        }
    }

    private static long GetByteCount(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : System.Text.Encoding.UTF8.GetByteCount(value);
    }

    private static long GetBase64EncodedSize(long rawBytes)
    {
        if (rawBytes <= 0)
        {
            return 0;
        }

        return ((rawBytes + 2) / 3) * 4;
    }
}
