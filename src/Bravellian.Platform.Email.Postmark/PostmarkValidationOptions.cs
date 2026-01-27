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

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Configures Postmark-specific validation rules.
/// </summary>
public sealed class PostmarkValidationOptions
{
    /// <summary>
    /// Gets or sets the maximum total message size (body + base64 attachments), in bytes.
    /// </summary>
    public long MaxMessageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum size for each body (text or HTML), in bytes.
    /// </summary>
    public long MaxBodyBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum size for a single attachment (raw bytes).
    /// </summary>
    public long? MaxAttachmentBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether forbidden file extensions are rejected.
    /// </summary>
    public bool EnforceForbiddenExtensions { get; set; } = true;

    /// <summary>
    /// Gets or sets the forbidden file extensions.
    /// </summary>
    public IReadOnlyCollection<string> ForbiddenExtensions { get; set; } = DefaultForbiddenExtensions;

    /// <summary>
    /// Gets the default forbidden file extensions.
    /// </summary>
    public static IReadOnlyCollection<string> DefaultForbiddenExtensions { get; } = new[]
    {
        ".vbs",
        ".exe",
        ".bin",
        ".bat",
        ".chm",
        ".com",
        ".cpl",
        ".crt",
        ".hlp",
        ".hta",
        ".inf",
        ".ins",
        ".isp",
        ".jse",
        ".lnk",
        ".mdb",
        ".pcd",
        ".pif",
        ".reg",
        ".scr",
        ".sct",
        ".shs",
        ".vbe",
        ".vba",
        ".wsf",
        ".wsh",
        ".wsl",
        ".msc",
        ".msi",
        ".msp",
        ".mst",
    };
}
