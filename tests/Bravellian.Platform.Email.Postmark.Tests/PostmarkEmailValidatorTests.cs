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
using Bravellian.Platform.Email.Postmark;

namespace Bravellian.Platform.Email.Postmark.Tests;

public sealed class PostmarkEmailValidatorTests
{
    /// <summary>When validate Rejects Body Over Limit, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for validate Rejects Body Over Limit.</intent>
    /// <scenario>Given validate Rejects Body Over Limit.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void Validate_RejectsBodyOverLimit()
    {
        var validator = new PostmarkEmailValidator(new PostmarkValidationOptions
        {
            MaxBodyBytes = 5,
            MaxMessageBytes = 100
        });
        var message = CreateMessage(textBody: "123456");

        var result = validator.Validate(message);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain("Text body exceeds maximum of 5 bytes.");
    }

    /// <summary>When validate Rejects Forbidden Extensions, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for validate Rejects Forbidden Extensions.</intent>
    /// <scenario>Given validate Rejects Forbidden Extensions.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void Validate_RejectsForbiddenExtensions()
    {
        var validator = new PostmarkEmailValidator(new PostmarkValidationOptions
        {
            MaxMessageBytes = 100,
            MaxBodyBytes = 100
        });
        var attachment = new EmailAttachment("malware.exe", "application/octet-stream", new byte[] { 1, 2, 3 });
        var message = CreateMessage(attachments: new[] { attachment });

        var result = validator.Validate(message);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain("Attachment 'malware.exe' has forbidden extension '.exe'.");
    }

    /// <summary>When validate Rejects Total Size Over Limit, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for validate Rejects Total Size Over Limit.</intent>
    /// <scenario>Given validate Rejects Total Size Over Limit.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void Validate_RejectsTotalSizeOverLimit()
    {
        var validator = new PostmarkEmailValidator(new PostmarkValidationOptions
        {
            MaxMessageBytes = 10,
            MaxBodyBytes = 100
        });
        var attachment = new EmailAttachment("tiny.txt", "text/plain", new byte[] { 1 });
        var message = CreateMessage(textBody: "123456789", attachments: new[] { attachment });

        var result = validator.Validate(message);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain("Total message size exceeds maximum of 10 bytes.");
    }

    /// <summary>When validate Uses Base64 Encoded Attachment Size For Total, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for validate Uses Base64 Encoded Attachment Size For Total.</intent>
    /// <scenario>Given validate Uses Base64 Encoded Attachment Size For Total.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void Validate_UsesBase64EncodedAttachmentSizeForTotal()
    {
        var validator = new PostmarkEmailValidator(new PostmarkValidationOptions
        {
            MaxMessageBytes = 3,
            MaxBodyBytes = 100
        });
        var attachment = new EmailAttachment("size.bin", "application/octet-stream", new byte[] { 1, 2, 3 });
        var message = CreateMessage(attachments: new[] { attachment });

        var result = validator.Validate(message);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain("Total message size exceeds maximum of 3 bytes.");
    }

    private static OutboundEmailMessage CreateMessage(
        string? textBody = "Hello",
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        return new OutboundEmailMessage(
            "key-1",
            new EmailAddress("sender@acme.test"),
            new[] { new EmailAddress("recipient@acme.test") },
            "Subject",
            textBody,
            htmlBody: null,
            attachments: attachments);
    }
}
