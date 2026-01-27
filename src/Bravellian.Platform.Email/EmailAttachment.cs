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
/// Represents an email attachment.
/// </summary>
public sealed record EmailAttachment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailAttachment"/> class.
    /// </summary>
    /// <param name="fileName">Attachment file name.</param>
    /// <param name="contentType">Attachment content type.</param>
    /// <param name="contentBytes">Attachment content bytes.</param>
    /// <param name="contentId">Optional content identifier.</param>
    public EmailAttachment(string fileName, string contentType, byte[] contentBytes, string? contentId = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (contentBytes is null || contentBytes.Length == 0)
        {
            throw new ArgumentException("Content bytes are required.", nameof(contentBytes));
        }

        FileName = fileName;
        ContentType = contentType;
        ContentBytes = contentBytes;
        ContentId = string.IsNullOrWhiteSpace(contentId) ? null : contentId;
    }

    /// <summary>
    /// Gets the attachment file name.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the attachment content type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the attachment content bytes.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Attachments require raw byte access.")]
    public byte[] ContentBytes { get; }

    /// <summary>
    /// Gets the optional content identifier.
    /// </summary>
    public string? ContentId { get; }
}


